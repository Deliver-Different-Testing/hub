using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hub.Interfaces;
using Hub.Shared;
using Hub.ViewModels;

namespace Hub.Controllers;

public class AccountController(
    IConnectionStringManager connectionStringManager,
    IDespatchRepository despatchRepository,
    IAuthenticationRepository authenticationRepository,
    HttpClient httpClient) : Controller
{
    // GET: /Account/Login
    [AllowAnonymous]
    public ActionResult Login(string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.IsValid = true;
        return View();
    }

// POST: /Account/Login
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult> Login(LoginViewModel model, string? returnUrl)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.LoginFailed = true;
            return View(model);
        }

        // Check if user is already authenticated as someone else
        if (User.Identity?.IsAuthenticated == true)
        {
            var currentEmail = User.FindFirst(ClaimTypes.Name)?.Value;
            if (!string.IsNullOrEmpty(currentEmail) &&
                !currentEmail.Equals(model.Email, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("User {CurrentEmail} attempting to login as {ModelEmail} without logging out first",
                    currentEmail, model.Email);
                ViewBag.LoginFailed = true;
                ModelState.AddModelError(string.Empty,
                    $"You are currently logged in as {currentEmail}. Please logout first before logging in as a different user.");
                return View(model);
            }
        }

        // Get user by email and login type (IsCourierLogin determines if we look for courier or staff account)
        var masterUser = await authenticationRepository.GetUserByEmail(model.Email, model.IsCourierLogin);

        if (masterUser == null)
        {
            Log.Debug("Failed to find user {ModelEmail} with IsCourier={ModelIsCourierLogin}", model.Email,
                model.IsCourierLogin);
            ViewBag.LoginFailed = true;
            var loginTypeText = model.IsCourierLogin ? "Courier Login" : "Staff Login";
            ModelState.AddModelError(string.Empty, $"Invalid login attempt. No {loginTypeText} account found for this email.");
            return View(model);
        }

        if (masterUser.CurrentTenant == null)
        {
            Log.Debug("Current Tenant Not Set for user {ModelEmail}", model.Email);
            ViewBag.LoginFailed = true;
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        if (!VerifyPassword(model.Password, masterUser.Salt, masterUser.Password, masterUser.IsLegacyHash))
        {
            Log.Debug("Failed to authenticate user {ModelEmail}. Invalid password.", model.Email);
            ViewBag.LoginFailed = true;
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        if (masterUser.IsLegacyHash)
            await UpgradeLegacyHashAsync(masterUser, model.Password);

        SetTenantConnectionString(masterUser.CurrentTenant.Dbconnection);

        // Fetch AccountsMode once for use in all code paths
        var accountsMode = await despatchRepository.GetAccountsModeAsync();

        // For courier users, validate that tucCourier record exists in Despatch DB
        var isCourier = masterUser.IsCourier ?? false;

        if (isCourier)
        {
            var courierId = await despatchRepository.ValidateCourierByEmail(model.Email);
            if (!courierId.HasValue)
            {
                Log.Warning(
                    "User {ModelEmail} is marked as courier but no active tucCourier record found in Despatch DB",
                    model.Email);
                ViewBag.LoginFailed = true;
                ModelState.AddModelError(string.Empty, "Invalid login attempt. Courier account not properly configured.");
                return View(model);
            }

            Log.Information("Courier validated with ID: {CourierId}", courierId.Value);

            // Courier users don't need staff user validation - skip to claims generation with default values
            var claims = GenerateClaims(new ClaimsInput(
                Email: model.Email,
                UserId: masterUser.UserId,
                CurrentTenantId: masterUser.CurrentTenant.TenantId,
                ContactId: "0",
                ClientId: "0",
                StaffId: string.Empty,
                Connection: masterUser.CurrentTenant.Dbconnection,
                RememberMe: model.RememberMe,
                CountryCode: masterUser.CurrentTenant.CountryCode,
                TimeZone: masterUser.CurrentTenant.TimeZone,
                TenantCode: masterUser.CurrentTenant.Code ?? string.Empty,
                InternalTenantUser: false,
                IsCourier: true,
                CourierId: courierId,
                AccountsMode: accountsMode
            ));

            await SignInUserAsync(claims, model.RememberMe);

            // Courier users stay on Hub to choose between Courier Portal and AfterHours
            Log.Information("Courier user {ModelEmail} logged in successfully, redirecting to Hub", model.Email);
        }
        else
        {
            // For non-courier users, validate staff login in Despatch DB
            var user = await despatchRepository.FetchUserByUsername(model.Email);

            if (user == null)
            {
                Log.Debug("Failed to authenticate Despatch User {ModelEmail}. Invalid username or password.",
                    model.Email);
                ViewBag.LoginFailed = true;
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            await despatchRepository.UpdateUserAccessedAsync(user.UcctId, model.RememberMe, masterUser.CurrentTenant.TenantId);

            var claims = GenerateClaims(new ClaimsInput(
                Email: model.Email,
                UserId: masterUser.UserId,
                CurrentTenantId: masterUser.CurrentTenant.TenantId,
                ContactId: user.UcctId.ToString(),
                ClientId: user.UcctClientId?.ToString() ?? "0",
                StaffId: user.StaffId?.ToString() ?? string.Empty,
                Connection: masterUser.CurrentTenant.Dbconnection,
                RememberMe: model.RememberMe,
                CountryCode: masterUser.CurrentTenant.CountryCode,
                TimeZone: masterUser.CurrentTenant.TimeZone,
                TenantCode: masterUser.CurrentTenant.Code ?? string.Empty,
                InternalTenantUser: user.UcctClient.UcclInternal,
                AccountsMode: accountsMode
            ));

            await SignInUserAsync(claims, model.RememberMe);

            // Special redirect for asure@urgent.co.nz to booking app with /asure param
            if (!model.Email.Equals("asure@urgent.co.nz", StringComparison.OrdinalIgnoreCase)
                || !(masterUser.CurrentTenant.Code?.Equals("urgent", StringComparison.OrdinalIgnoreCase) ?? false))
                return RedirectToAction("Index", "Home");
            var tenantUrl = Environment.GetEnvironmentVariable("TenantURL");
            if (string.IsNullOrEmpty(tenantUrl)) return RedirectToAction("Index", "Home");
            var bookingUrl = tenantUrl.Replace("app_name", "booking") + "/#/asure";
            Log.Information("Redirecting user {ModelEmail} to booking app with asure param: {BookingUrl}",
                model.Email, bookingUrl);
            return Redirect(bookingUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    //
    // GET: /Account/CreditCard
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult> CreditCard(string token)
    {
        var expectedToken = Environment.GetEnvironmentVariable("CreditCardToken") ?? string.Empty;
        if (string.IsNullOrEmpty(expectedToken) || token != expectedToken)
        {
            Log.Warning("CreditCard auto-login rejected: invalid or missing token");
            return RedirectToAction("Login");
        }

        var presetEmail = Environment.GetEnvironmentVariable("CreditCardEmail") ?? string.Empty;
        var presetPassword = Environment.GetEnvironmentVariable("CreditCardPassword") ?? string.Empty;

        if (string.IsNullOrEmpty(presetEmail) || string.IsNullOrEmpty(presetPassword))
        {
            Log.Warning("Credit card auto-login credentials not configured in environment variables");
            return RedirectToAction("Login", new { error = "Auto-login not configured." });
        }

        // Create a login model with preset credentials
        var model = new LoginViewModel
        {
            Email = presetEmail,
            Password = presetPassword,
            IsCourierLogin = false, // Set to true if this should be a courier login
            RememberMe = false
        };

        // Get user by email and login type
        var masterUser = await authenticationRepository.GetUserByEmail(model.Email, model.IsCourierLogin);

        if (masterUser == null)
        {
            Log.Warning("Credit card auto-login failed: user {ModelEmail} not found", model.Email);
            return RedirectToAction("Login", new { error = "Auto-login failed. Please login manually." });
        }

        if (masterUser.CurrentTenant == null)
        {
            Log.Warning("Credit card auto-login failed: Current Tenant Not Set for user {ModelEmail}", model.Email);
            return RedirectToAction("Login", new { error = "Auto-login failed. Please login manually." });
        }

        if (!VerifyPassword(model.Password, masterUser.Salt, masterUser.Password, masterUser.IsLegacyHash))
        {
            Log.Warning("Credit card auto-login failed: Invalid password for {ModelEmail}", model.Email);
            return RedirectToAction("Login", new { error = "Auto-login failed. Please login manually." });
        }

        if (masterUser.IsLegacyHash)
            await UpgradeLegacyHashAsync(masterUser, model.Password);

        SetTenantConnectionString(masterUser.CurrentTenant.Dbconnection);

        var accountsMode = await despatchRepository.GetAccountsModeAsync();
        var isCourier = masterUser.IsCourier ?? false;

        var user = await despatchRepository.FetchUserByUsername(model.Email);

        if (user == null)
        {
            Log.Warning("Credit card auto-login failed: Despatch user {ModelEmail} not found", model.Email);
            return RedirectToAction("Login", new { error = "Auto-login failed. Please login manually." });
        }

        await despatchRepository.UpdateUserAccessedAsync(user.UcctId, false, masterUser.CurrentTenant.TenantId);

        var claims = GenerateClaims(new ClaimsInput(
            Email: model.Email,
            UserId: masterUser.UserId,
            CurrentTenantId: masterUser.CurrentTenant.TenantId,
            ContactId: user.UcctId.ToString(),
            ClientId: user.UcctClientId?.ToString() ?? "0",
            StaffId: user.StaffId?.ToString() ?? string.Empty,
            Connection: masterUser.CurrentTenant.Dbconnection,
            RememberMe: false,
            CountryCode: masterUser.CurrentTenant.CountryCode,
            TimeZone: masterUser.CurrentTenant.TimeZone,
            TenantCode: masterUser.CurrentTenant.Code ?? string.Empty,
            InternalTenantUser: user.UcctClient.UcclInternal,
            IsCourier: isCourier,
            AccountsMode: accountsMode
        ));

        await SignInUserAsync(claims, false);

        Log.Information("Credit card user {ModelEmail} auto-logged in successfully", model.Email);

        var tenantUrl = Environment.GetEnvironmentVariable("TenantURL");
        if (string.IsNullOrEmpty(tenantUrl)) return RedirectToAction("Index", "Home");
        var bookingUrl = tenantUrl.Replace("app_name", "booking");
        Log.Information("Redirecting user {ModelEmail} to booking app with creca param: {BookingUrl}", model.Email,
            bookingUrl);
        return Redirect(bookingUrl);
    }

    [AllowAnonymous]
    public async Task<ActionResult> ResetPassword(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            Log.Debug("Failed to find user via reset key {Code}", code);
            return RedirectToActionPermanent("Index", "Home");
        }

        var masterUser = await authenticationRepository.GetUserByResetKey(code);
        if (masterUser == null) return RedirectToActionPermanent("Index", "Home");

        var model = new ResetPasswordViewModel
        {
            Email = masterUser.Email,
            Code = code
        };


        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.ResetFailed = true;
            return View(model);
        }

        var masterUser = await authenticationRepository.GetUserByResetKey(model.Code);
        if (masterUser == null)
        {
            Log.Debug("Failed to find user via reset key {ModelCode}", model.Code);
            return RedirectToActionPermanent("Index", "Home");
        }

        var result = PasswordHelper.SaltHashNewPassword(model.Password);

        masterUser.Password = result.Hashed;
        masterUser.Salt = result.Salt;
        masterUser.ResetKey = null;
        await authenticationRepository.SaveAsync();

        SetTenantConnectionString(masterUser.CurrentTenant.Dbconnection);

        var accountsMode = await despatchRepository.GetAccountsModeAsync();

        var user = await despatchRepository.FetchUserByUsername(model.Email);

        if (user == null)
        {
            Log.Debug("Failed to authenticate Despatch User {ModelEmail}. Invalid username or password.", model.Email);
            return View(model);
        }

        await despatchRepository.UpdateUserAccessedAsync(user.UcctId, false, masterUser.CurrentTenant.TenantId);

        var claims = GenerateClaims(new ClaimsInput(
            Email: model.Email,
            UserId: masterUser.UserId,
            CurrentTenantId: masterUser.CurrentTenant.TenantId,
            ContactId: user.UcctId.ToString(),
            ClientId: user.UcctClientId?.ToString() ?? "0",
            StaffId: user.StaffId?.ToString() ?? string.Empty,
            Connection: masterUser.CurrentTenant.Dbconnection,
            RememberMe: false,
            CountryCode: masterUser.CurrentTenant.CountryCode,
            TimeZone: masterUser.CurrentTenant.TimeZone,
            TenantCode: masterUser.CurrentTenant.Code ?? string.Empty,
            InternalTenantUser: user.UcctClient.UcclInternal,
            IsCourier: masterUser.IsCourier ?? false,
            AccountsMode: accountsMode
        ));

        await SignInUserAsync(claims, false);

        // Special redirect for asure@urgent.co.nz to booking app with /asure param
        if (!model.Email.Equals("asure@urgent.co.nz", StringComparison.OrdinalIgnoreCase)
            || !(masterUser.CurrentTenant.Code?.Equals("urgent", StringComparison.OrdinalIgnoreCase) ?? false))
            return RedirectToAction("Index", "Home");
        var tenantUrl = Environment.GetEnvironmentVariable("TenantURL");
        if (string.IsNullOrEmpty(tenantUrl)) return RedirectToAction("Index", "Home");
        var bookingUrl = tenantUrl.Replace("app_name", "booking") + "/#/asure";
        Log.Information("Redirecting user {ModelEmail} to booking app with asure param: {BookingUrl}",
            model.Email, bookingUrl);
        return Redirect(bookingUrl);
    }

    //
    // GET: /Account/ForgotPassword
    [AllowAnonymous]
    public ActionResult ForgotPassword() => View();

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return Json(new { success = false, message = "Please check your input and try again." });


        var reCaptchaResponse = await VerifyReCaptcha(Request.Form["g-recaptcha-response"].ToString());

        if (!reCaptchaResponse.Success || reCaptchaResponse.Score < 0.5)
            return Json(new { success = false, message = "reCAPTCHA validation failed. Please try again." });

        // Check if email address exists in system
        var masterUser = await authenticationRepository.GetUserByEmail(model.Email);

        if (masterUser == null)
            return Json(new { success = false, message = "Please check your input and try again." });

        masterUser.ResetKey = Guid.NewGuid().ToString();
        await authenticationRepository.SaveAsync();
        var reply = Environment.GetEnvironmentVariable("ReplyEmail") ?? string.Empty;
        var baseLink = Environment.GetEnvironmentVariable("ResetBaseLink");
        var link = $"{baseLink}?code={masterUser.ResetKey}";

        SetTenantConnectionString(masterUser.CurrentTenant.Dbconnection);

        var user = await despatchRepository.FetchUserByUsername(model.Email);

        if (user == null)
        {
            Log.Debug("Failed to authenticate Despatch User {ModelEmail}. Invalid username or password.",
                model.Email);
            return Json(new { success = false, message = "Reset failed due to contact validation failure" });
        }

        await despatchRepository.InitiatePasswordReset(user.UcctId, model.Email, reply, link);


        return Json(new { success = true, message = "Password reset instructions have been sent to your email." });
    }

    private async Task<ReCaptchaResponse> VerifyReCaptcha(string token)
    {
        var secretKey = Environment.GetEnvironmentVariable("GoogleRecaptchaSecretKey") ?? string.Empty;
        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("secret", secretKey),
            new KeyValuePair<string, string>("response", token)
        ]);

        var response = await httpClient.PostAsync("https://www.google.com/recaptcha/api/siteverify", content);
        var responseString = await response.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<ReCaptchaResponse>(responseString, options)
               ?? new ReCaptchaResponse();
    }


    public class ReCaptchaResponse
    {
        [JsonPropertyName("success")] public bool Success { get; init; }

        [JsonPropertyName("score")] public double Score { get; init; }

        [JsonPropertyName("action")] public string Action { get; init; } = string.Empty;

        [JsonPropertyName("challenge_ts")] public DateTime ChallengeTs { get; init; }

        [JsonPropertyName("hostname")] public string Hostname { get; init; } = string.Empty;
    }

    private record ClaimsInput(
        string Email,
        int UserId,
        int CurrentTenantId,
        string ContactId,
        string ClientId,
        string StaffId,
        string Connection,
        bool RememberMe,
        string CountryCode,
        string TimeZone,
        string TenantCode,
        bool InternalTenantUser,
        bool IsCourier = false,
        int? CourierId = null,
        int? AccountsMode = null);

    private static List<Claim> GenerateClaims(ClaimsInput input) =>
    [
        new(ClaimTypes.Name, input.Email),
        new("UserID", input.UserId.ToString()),
        new("CurrentTenantID", input.CurrentTenantId.ToString()),
        new("ContactID", input.ContactId),
        new("ClientID", input.ClientId),
        new("StaffID", input.StaffId),
        new("Connection", input.Connection),
        new("CountryCode", input.CountryCode),
        new("TimeZone", input.TimeZone),
        new("TenantCode", input.TenantCode),
        new("RememberMe", input.RememberMe.ToString()),
        new("Internal", input.InternalTenantUser.ToString()),
        new("IsCourier", input.IsCourier.ToString()),
        new("CourierID", input.CourierId?.ToString() ?? string.Empty),
        new("AccountsMode", input.AccountsMode?.ToString() ?? "1")
    ];

    private void SetTenantConnectionString(string dbConnection)
    {
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
            throw new InvalidOperationException("Could not find a environment variable string named 'SQLCredentials'.");
        connectionStringManager.SetConnectionString(dbConnection + credentials);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Obsolete", "CS0618", Justification = "Legacy hash needed to verify users not yet upgraded")]
    private static bool VerifyPassword(string password, string salt, string storedHash, bool isLegacy)
    {
        var hash = isLegacy
            ? PasswordHelper.HashPasswordLegacy(password, salt)
            : PasswordHelper.HashPassword(password, salt);
        return hash == storedHash;
    }

    private async Task UpgradeLegacyHashAsync(Models.Master.User masterUser, string password)
    {
        var newHash = PasswordHelper.HashPassword(password, masterUser.Salt);
        masterUser.Password = newHash;
        masterUser.IsLegacyHash = false;
        await authenticationRepository.SaveAsync();
    }

    private async Task SignInUserAsync(List<Claim> claims, bool isPersistent)
    {
        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var authProperties = new AuthenticationProperties
        {
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
            IsPersistent = isPersistent
        };

        await HttpContext.SignInAsync(
            "Identity.Application",
            new ClaimsPrincipal(claimsIdentity),
            authProperties);
    }

    [Authorize]
    public async Task<IActionResult> Logout()
    {
        // Clear the existing external cookie
        await HttpContext.SignOutAsync("Identity.Application");

        HttpContext.Session.Clear();

        return RedirectToAction("login", "Account");
    }


    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCurrentTenant([FromBody] TenantUpdateModel? model)
    {
        var userId = User.FindFirst("UserID")?.Value;
        if (userId == null)
            return Json(new { success = false, message = "User not found" });

        if (model == null || model.TenantId == 0)
            return Json(new { success = false, message = "Invalid tenant ID" });

        var success = await authenticationRepository.UpdateCurrentTenantIdAsync(int.Parse(userId), model.TenantId);

        if (!success) return Json(new { success = false, message = "Update database failed" });

        var masterUser = await authenticationRepository.GetUserById(int.Parse(userId));

        if (masterUser == null)
        {
            Log.Debug("Failed to find master user {UserId}", userId);
            return Json(new { success = false, message = "User not found" });
        }

        if (masterUser.CurrentTenant == null)
        {
            Log.Debug("Current Tenant Not Set for user {UserId}", userId);
            return Json(new { success = false, message = $"Current Tenant Not Set for user {userId}" });
        }

        // Defensive consistency check: the just-persisted CurrentTenant must match the
        // requested tenant. If it doesn't, the update silently failed (stale EF entity,
        // SaveChanges no-op, etc.) and we must NOT mint a cookie that mixes the new
        // TenantId claim with the old tenant's other attributes — that exact mismatch is
        // what produced the OTGCargo cache poisoning incident on 2026-04-14.
        if (masterUser.CurrentTenant.TenantId != model.TenantId)
        {
            Log.Error("Tenant switch did not persist: requested {Requested}, persisted {Persisted} for user {UserId}",
                model.TenantId, masterUser.CurrentTenant.TenantId, userId);
            return Json(new { success = false, message = "Tenant update did not persist" });
        }

        //Changed Tenant - switch connection
        Log.Debug("Changed Current Tenant for user {UserId}", userId);
        SetTenantConnectionString(masterUser.CurrentTenant.Dbconnection);

        var accountsMode = await despatchRepository.GetAccountsModeAsync();

        var email = User.Identity?.Name ?? string.Empty;
        var user = await despatchRepository.FetchUserByUsername(email);

        if (user == null)
        {
            Log.Debug("Failed to authenticate Despatch User {IdentityName}. Invalid username.", email);
            return Json(new { success = false, message = "Despatch User not found" });
        }

        var rememberMe = bool.Parse(User.FindFirst("RememberMe")?.Value ?? "false");
        await despatchRepository.UpdateUserAccessedAsync(user.UcctId, rememberMe, masterUser.CurrentTenant.TenantId);
        Log.Debug("About to write Claim details. ContactID: {ToString}", user.UcctId.ToString());
        Log.Debug("About to write Claim details. Connection: {CurrentTenantDbconnection}",
            masterUser.CurrentTenant.Dbconnection);

        var claims = GenerateClaims(new ClaimsInput(
            Email: User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty,
            UserId: masterUser.UserId,
            CurrentTenantId: masterUser.CurrentTenant.TenantId,
            ContactId: user.UcctId.ToString(),
            ClientId: user.UcctClientId?.ToString() ?? "0",
            StaffId: user.StaffId?.ToString() ?? string.Empty,
            Connection: masterUser.CurrentTenant.Dbconnection,
            RememberMe: rememberMe,
            CountryCode: masterUser.CurrentTenant.CountryCode,
            TimeZone: masterUser.CurrentTenant.TimeZone,
            TenantCode: masterUser.CurrentTenant.Code ?? string.Empty,
            InternalTenantUser: user.UcctClient.UcclInternal,
            IsCourier: masterUser.IsCourier ?? false,
            AccountsMode: accountsMode
        ));

        await SignInUserAsync(claims, rememberMe);

        // Phase 2: build a short-lived SSO token and return the destination Hub URL.
        // The frontend redirects the browser there; the destination Hub validates the
        // token and issues a fresh cookie scoped to its own subdomain. This restores
        // "URL matches active tenant" once the user lands.
        var destHost = BuildDestinationHubHost(HttpContext.Request.Host.Host, masterUser.CurrentTenant.Code ?? string.Empty);
        if (destHost == null)
        {
            // Couldn't infer destination — return success without a redirectUrl so
            // the frontend falls back to in-place reload (Phase 1 behaviour).
            return Json(new { success = true });
        }

        var token = JsonSerializer.Serialize(new
        {
            UserId = masterUser.UserId,
            TenantId = masterUser.CurrentTenant.TenantId,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds()
        });
        var encryptedToken = EncryptClaims(token, Environment.GetEnvironmentVariable("ClaimsKey") ?? string.Empty);
        var redirectUrl = $"https://{destHost}/Account/AcceptTenantSwitchToken?t={Uri.EscapeDataString(encryptedToken)}";

        return Json(new { success = true, redirectUrl });
    }

    [HttpGet("Account/AcceptTenantSwitchToken")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptTenantSwitchToken([FromQuery] string t)
    {
        if (string.IsNullOrWhiteSpace(t))
        {
            Log.Warning("AcceptTenantSwitchToken: empty token");
            return RedirectToAction("Login");
        }

        TenantSwitchTokenPayload? payload;
        try
        {
            var decrypted = DecryptClaims(t, Environment.GetEnvironmentVariable("ClaimsKey") ?? string.Empty);
            payload = JsonSerializer.Deserialize<TenantSwitchTokenPayload>(decrypted,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AcceptTenantSwitchToken: failed to decrypt or parse token");
            return RedirectToAction("Login");
        }

        if (payload == null || payload.UserId <= 0 || payload.TenantId <= 0)
        {
            Log.Warning("AcceptTenantSwitchToken: token payload invalid");
            return RedirectToAction("Login");
        }

        if (payload.ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            Log.Warning("AcceptTenantSwitchToken: token expired (issued for user {UserId}, tenant {TenantId})",
                payload.UserId, payload.TenantId);
            return RedirectToAction("Login");
        }

        var masterUser = await authenticationRepository.GetUserById(payload.UserId);
        if (masterUser?.CurrentTenant == null)
        {
            Log.Warning("AcceptTenantSwitchToken: user {UserId} or current tenant not found", payload.UserId);
            return RedirectToAction("Login");
        }

        // Verify this Hub serves the tenant the token was issued for. Prevents a token
        // generated for one tenant being replayed at another tenant's Hub.
        var hostTenant = ExtractTenantFromHost(HttpContext.Request.Host.Host);
        if (hostTenant == null ||
            !string.Equals(hostTenant, masterUser.CurrentTenant.Code, StringComparison.OrdinalIgnoreCase) ||
            masterUser.CurrentTenant.TenantId != payload.TenantId)
        {
            Log.Warning("AcceptTenantSwitchToken: token tenant {TokenTenant}/{TokenCode} does not match host {HostTenant} (user {UserId})",
                payload.TenantId, masterUser.CurrentTenant.Code, hostTenant, payload.UserId);
            return RedirectToAction("Login");
        }

        SetTenantConnectionString(masterUser.CurrentTenant.Dbconnection);

        var accountsMode = await despatchRepository.GetAccountsModeAsync();
        var user = await despatchRepository.FetchUserByUsername(masterUser.Email);
        if (user == null)
        {
            Log.Warning("AcceptTenantSwitchToken: despatch user not found for {Email}", masterUser.Email);
            return RedirectToAction("Login");
        }

        await despatchRepository.UpdateUserAccessedAsync(user.UcctId, false, masterUser.CurrentTenant.TenantId);

        var claims = GenerateClaims(new ClaimsInput(
            Email: masterUser.Email,
            UserId: masterUser.UserId,
            CurrentTenantId: masterUser.CurrentTenant.TenantId,
            ContactId: user.UcctId.ToString(),
            ClientId: user.UcctClientId?.ToString() ?? "0",
            StaffId: user.StaffId?.ToString() ?? string.Empty,
            Connection: masterUser.CurrentTenant.Dbconnection,
            RememberMe: false,
            CountryCode: masterUser.CurrentTenant.CountryCode,
            TimeZone: masterUser.CurrentTenant.TimeZone,
            TenantCode: masterUser.CurrentTenant.Code ?? string.Empty,
            InternalTenantUser: user.UcctClient.UcclInternal,
            IsCourier: masterUser.IsCourier ?? false,
            AccountsMode: accountsMode
        ));

        await SignInUserAsync(claims, false);

        return RedirectToAction("Index", "Home");
    }

    private sealed record TenantSwitchTokenPayload(int UserId, int TenantId, long ExpiresAt);

    /// <summary>
    /// Replaces the tenant segment in the current request host with the destination
    /// tenant's code, returning the resulting Hub host. Expects the host to follow
    /// the pattern app.tenant.[env.]deliverdifferent.com.
    /// </summary>
    internal static string? BuildDestinationHubHost(string requestHost, string destinationTenantCode)
    {
        if (string.IsNullOrWhiteSpace(destinationTenantCode)) return null;
        if (string.IsNullOrEmpty(requestHost)) return null;
        if (!requestHost.EndsWith("deliverdifferent.com", StringComparison.OrdinalIgnoreCase)) return null;

        var parts = requestHost.Split('.');
        if (parts.Length < 4) return null;

        parts[0] = "hub";
        parts[1] = destinationTenantCode;
        return string.Join('.', parts);
    }

    /// <summary>
    /// Returns the tenant subdomain segment from a request host, or null if the host
    /// does not expose a tenant (e.g. localhost, internal IPs, generic environment hosts).
    /// </summary>
    internal static string? ExtractTenantFromHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return null;
        if (!host.EndsWith("deliverdifferent.com", StringComparison.OrdinalIgnoreCase)) return null;

        var parts = host.Split('.');
        if (parts.Length < 4) return null;

        var candidate = parts[1];
        return candidate is "staging" or "local" or "dev" ? null : candidate;
    }

    private static string EncryptClaims(string claims, string key)
    {
        using var aesAlg = Aes.Create();
        var keyBytes = Convert.FromBase64String(key);
        aesAlg.Key = keyBytes;
        // Generate a cryptographically secure random IV
        aesAlg.GenerateIV();
        var iv = aesAlg.IV;

        using var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
        using var msEncrypt = new MemoryStream();
        // Write the IV to the beginning of the stream
        msEncrypt.Write(iv, 0, iv.Length);
        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        using (var swEncrypt = new StreamWriter(csEncrypt))
            swEncrypt.Write(claims);

        return Convert.ToBase64String(msEncrypt.ToArray());
    }

    private static string DecryptClaims(string encryptedClaims, string key)
    {
        var fullCipherText = Convert.FromBase64String(encryptedClaims);
        using var aesAlg = Aes.Create();
        var keyBytes = Convert.FromBase64String(key);
        aesAlg.Key = keyBytes;

        // The IV was written at the beginning of the ciphertext during encryption.
        var ivLength = aesAlg.BlockSize / 8;
        if (fullCipherText.Length < ivLength)
            throw new ArgumentException("Encrypted payload too short to contain an IV.", nameof(encryptedClaims));

        var iv = new byte[ivLength];
        Array.Copy(fullCipherText, 0, iv, 0, ivLength);
        aesAlg.IV = iv;

        var cipherTextWithoutIv = new byte[fullCipherText.Length - ivLength];
        Array.Copy(fullCipherText, ivLength, cipherTextWithoutIv, 0, cipherTextWithoutIv.Length);

        using var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
        using var msDecrypt = new MemoryStream(cipherTextWithoutIv);
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var srDecrypt = new StreamReader(csDecrypt);
        return srDecrypt.ReadToEnd();
    }


    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateApiKey()
    {
        try
        {
            var email = GetClaim(ClaimTypes.Name);
            var clientId = GetClaim("ClientID");
            var tenantId = GetClaim("CurrentTenantID");
            var connection = GetClaim("Connection");
            var timeZone = GetClaim("TimeZone");
            var contactId = GetClaim("ContactID");
            var userId = GetClaim("UserID");
            var tenantCode = GetClaim("TenantCode");

            if (email == null || clientId == null || tenantId == null || connection == null ||
                timeZone == null || contactId == null || userId == null || tenantCode == null)
            {
                Log.Debug("Failed to generate API key: one or more required claims missing");
                return Json(new { success = false, message = "Failed to generate API key" });
            }

            var subAccounts = await despatchRepository.FetchSubAccountsAsync(int.Parse(clientId));

            var token = CreateApiToken(email, int.Parse(clientId), int.Parse(contactId), subAccounts,
                int.Parse(tenantId), connection, timeZone, tenantCode);
            var respToken = new JwtSecurityTokenHandler().WriteToken(token);
            var viewModel = new TenantUserSettingViewModel
            {
                Name = "APIKey",
                Value = respToken
            };

            await authenticationRepository.SaveUserSetting(viewModel, int.Parse(tenantId), int.Parse(userId));
            return Json(new { success = true, message = "Successfully generated API key", apiKey = respToken });

            string? GetClaim(string type) => User.FindFirst(type)?.Value;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate API Key: {ExMessage}", ex.Message);
            return Json(new { success = false, message = "Failed to generate API key" });
        }
    }


    private static JwtSecurityToken CreateApiToken(string name, int clientId, int contactId, string subAccounts,
        int tenantId,
        string connection, string tenantTimeZone, string tenantCode)
    {
        var symmetricSecurityKey =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("JWTSecretKey") ?? string.Empty));

        var sensitiveClaims = JsonSerializer.Serialize(new
        {
            ClientId = clientId.ToString(),
            SubAccounts = subAccounts,
            ContactId = contactId.ToString(),
            TenantId = tenantId.ToString(),
            Connection = connection,
            TimeZone = tenantTimeZone,
            TenantCode = tenantCode
        });
        var encryptedClaims = EncryptClaims(sensitiveClaims,
            Environment.GetEnvironmentVariable("ClaimsKey") ?? string.Empty);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, name),
            new Claim("SC", encryptedClaims)
        };

        return new JwtSecurityToken(
            issuer: Environment.GetEnvironmentVariable("Issuer"),
            audience: Environment.GetEnvironmentVariable("Audience"),
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7), // expires in 7 days by default, but we don't validate the expiry date
            signingCredentials: new SigningCredentials(symmetricSecurityKey, SecurityAlgorithms.HmacSha256)
        );
    }

    [Authorize]
    public async Task<ActionResult> Settings()
    {
        IReadOnlyList<TenantUserSettingViewModel> data = [];
        var email = HttpContext.User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value;

        if (email == null)
        {
            Log.Debug("Could not find user Email");
            return View(data);
        }

        var masterUser = await authenticationRepository.GetUserByEmail(email);

        if (masterUser == null)
        {
            Log.Debug("Failed to find user {Email}", email);
            return View(data);
        }

        data = await authenticationRepository.GetUserSettings(masterUser.CurrentTenant.TenantId, masterUser.UserId);

        return View(data);
    }
}
