using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Hub.Repositories;
using System.Linq;
using Hub.Shared;
using Hub.ViewModels;

namespace Hub.Controllers;

public class AccountController(
    IConnectionStringManager connectionStringManager,
    Repository despatchRepository,
    AuthenticationRepository authenticationRepository,
    HttpClient httpClient) : Controller
{
    // GET: /Account/Login
    [AllowAnonymous]
    public ActionResult Login(string returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.IsValid = true;
        return View();
    }

// POST: /Account/Login
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
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

        var salted = masterUser.Salt;
        var userPassword = masterUser.Password;

        if (masterUser.IsLegacyHash)
        {
            var hashedPassword = PasswordHelper.HashPasswordLegacy(model.Password, salted);

            if (hashedPassword != userPassword)
            {
                Log.Debug("Failed to authenticate user {ModelEmail}. Invalid password.", model.Email);
                ViewBag.LoginFailed = true;
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            // Generate new hash with the improved method
            var newHash = PasswordHelper.HashPassword(model.Password, salted);

            masterUser.Password = newHash;
            masterUser.IsLegacyHash = false;
            await authenticationRepository.SaveAsync();
        }
        else
        {
            var hashedPassword = PasswordHelper.HashPassword(model.Password, salted);

            if (hashedPassword != userPassword)
            {
                Log.Debug("Failed to authenticate user {ModelEmail}. Invalid password.", model.Email);
                ViewBag.LoginFailed = true;
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }
        }


        var connectionString = masterUser.CurrentTenant.Dbconnection;
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
        {
            throw new InvalidOperationException(
                "Could not find a environment variable string named 'SQLCredentials'.");
        }

        connectionStringManager.SetConnectionString(connectionString + credentials);

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
            var claims = GenerateClaims(
                model.Email,
                masterUser.UserId,
                masterUser.CurrentTenant.TenantId,
                "0", // No ContactID for courier
                "0", // No ClientID for courier
                string.Empty, // No StaffID for courier
                masterUser.CurrentTenant.Dbconnection,
                model.RememberMe,
                masterUser.CurrentTenant.CountryCode,
                masterUser.CurrentTenant.TimeZone,
                masterUser.CurrentTenant.Code ?? string.Empty,
                false, // Couriers are not internal tenant users
                true,
                courierId,
                accountsMode
            );

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

            despatchRepository.UpdateUserAccessed(user.UcctId, model.RememberMe);

            var claims = GenerateClaims(
                model.Email,
                masterUser.UserId,
                masterUser.CurrentTenant.TenantId,
                user.UcctId.ToString(),
                user.UcctClientId.ToString(),
                user.StaffId?.ToString() ?? string.Empty,
                masterUser.CurrentTenant.Dbconnection,
                model.RememberMe,
                masterUser.CurrentTenant.CountryCode,
                masterUser.CurrentTenant.TimeZone,
                masterUser.CurrentTenant.Code ?? string.Empty,
                user.UcctClient.UcclInternal,
                false,
                null,
                accountsMode
            );

            await SignInUserAsync(claims, model.RememberMe);

            // Special redirect for asure@urgent.co.nz to booking app with /asure param
            if (!model.Email.Equals("asure@urgent.co.nz", StringComparison.OrdinalIgnoreCase)
                || !masterUser.CurrentTenant.Code.Equals("urgent", StringComparison.OrdinalIgnoreCase))
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
    public async Task<ActionResult> CreditCard()
    {
        // Define the preset credentials
        const string presetEmail = "creditcard@urgent.co.nz";
        const string presetPassword = "AiZAy671pL";

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

        var salted = masterUser.Salt;
        var userPassword = masterUser.Password;

        // Verify password
        string hashedPassword;
        if (masterUser.IsLegacyHash)
        {
            hashedPassword = PasswordHelper.HashPasswordLegacy(model.Password, salted);

            if (hashedPassword != userPassword)
            {
                Log.Warning("Credit card auto-login failed: Invalid password for {ModelEmail}", model.Email);
                return RedirectToAction("Login", new { error = "Auto-login failed. Please login manually." });
            }

            // Upgrade to new hash
            var newHash = PasswordHelper.HashPassword(model.Password, salted);
            masterUser.Password = newHash;
            masterUser.IsLegacyHash = false;
            await authenticationRepository.SaveAsync();
        }
        else
        {
            hashedPassword = PasswordHelper.HashPassword(model.Password, salted);

            if (hashedPassword != userPassword)
            {
                Log.Warning("Credit card auto-login failed: Invalid password for {ModelEmail}", model.Email);
                return RedirectToAction("Login", new { error = "Auto-login failed. Please login manually." });
            }
        }

        // Set connection string
        var connectionString = masterUser.CurrentTenant.Dbconnection;
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
        {
            throw new InvalidOperationException(
                "Could not find a environment variable string named 'SQLCredentials'.");
        }

        connectionStringManager.SetConnectionString(connectionString + credentials);

        var accountsMode = await despatchRepository.GetAccountsModeAsync();
        var isCourier = masterUser.IsCourier ?? false;

        var user = await despatchRepository.FetchUserByUsername(model.Email);

        if (user == null)
        {
            Log.Warning("Credit card auto-login failed: Despatch user {ModelEmail} not found", model.Email);
            return RedirectToAction("Login", new { error = "Auto-login failed. Please login manually." });
        }

        despatchRepository.UpdateUserAccessed(user.UcctId, false);

        var claims = GenerateClaims(
            model.Email,
            masterUser.UserId,
            masterUser.CurrentTenant.TenantId,
            user.UcctId.ToString(),
            user.UcctClientId.ToString(),
            user.StaffId?.ToString() ?? string.Empty,
            masterUser.CurrentTenant.Dbconnection,
            false,
            masterUser.CurrentTenant.CountryCode,
            masterUser.CurrentTenant.TimeZone,
            masterUser.CurrentTenant.Code ?? string.Empty,
            user.UcctClient.UcclInternal,
            isCourier,
            null,
            accountsMode
        );

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

        var connectionString = masterUser.CurrentTenant.Dbconnection;
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
        {
            throw new InvalidOperationException(
                "Could not find a environment variable string named 'SQLCredentials'.");
        }

        connectionStringManager.SetConnectionString(connectionString + credentials);

        // Fetch AccountsMode once for use in claims generation
        var accountsMode = await despatchRepository.GetAccountsModeAsync();

        var user = await despatchRepository.FetchUserByUsername(model.Email);

        if (user == null)
        {
            Log.Debug("Failed to authenticate Despatch User {ModelEmail}. Invalid username or password.", model.Email);
            return View(model);
        }

        despatchRepository.UpdateUserAccessed(user.UcctId, false);

        var claims = GenerateClaims(
            model.Email,
            masterUser.UserId,
            masterUser.CurrentTenant.TenantId,
            user.UcctId.ToString(),
            user.UcctClientId.ToString(),
            user.StaffId?.ToString() ?? string.Empty,
            masterUser.CurrentTenant.Dbconnection,
            false,
            masterUser.CurrentTenant.CountryCode,
            masterUser.CurrentTenant.TimeZone,
            masterUser.CurrentTenant.Code ?? string.Empty,
            user.UcctClient.UcclInternal,
            masterUser.IsCourier ?? false,
            null,
            accountsMode
        );

        await SignInUserAsync(claims, false);

        // Special redirect for asure@urgent.co.nz to booking app with /asure param
        if (!model.Email.Equals("asure@urgent.co.nz", StringComparison.OrdinalIgnoreCase)
            || !masterUser.CurrentTenant.Code.Equals("urgent", StringComparison.OrdinalIgnoreCase))
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
    public async Task<ActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return Json(new { success = false, message = "Please check your input and try again." });


        var reCaptchaResponse = await VerifyReCaptcha(Request.Form["g-recaptcha-response"]);

        if (!reCaptchaResponse.Success || reCaptchaResponse.Score < 0.5)
            return Json(new { success = false, message = "reCAPTCHA validation failed. Please try again." });

        // Check if email address exists in system
        var masterUser = await authenticationRepository.GetUserByEmail(model.Email);

        if (masterUser == null)
            return Json(new { success = false, message = "Please check your input and try again." });

        masterUser.ResetKey = Guid.NewGuid().ToString();
        await authenticationRepository.SaveAsync();
        var reply = Environment.GetEnvironmentVariable("ReplyEmail");
        var baseLink = Environment.GetEnvironmentVariable("ResetBaseLink");
        var link = $"{baseLink}?code={masterUser.ResetKey}";

        var connectionString = masterUser.CurrentTenant.Dbconnection;
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
        {
            throw new InvalidOperationException(
                "Could not find a environment variable string named 'SQLCredentials'.");
        }

        connectionStringManager.SetConnectionString(connectionString + credentials);


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
        var secretKey = Environment.GetEnvironmentVariable("GoogleRecaptchaSecretKey");
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

        return JsonSerializer.Deserialize<ReCaptchaResponse>(responseString, options);
    }


    public class ReCaptchaResponse
    {
        [JsonPropertyName("success")] public bool Success { get; init; }

        [JsonPropertyName("score")] public double Score { get; init; }

        [JsonPropertyName("action")] public string Action { get; init; }

        [JsonPropertyName("challenge_ts")] public DateTime ChallengeTs { get; init; }

        [JsonPropertyName("hostname")] public string Hostname { get; init; }
    }

    private static List<Claim> GenerateClaims(string email, int userId, int currentTenantId, string contactId,
        string clientId,
        string staffId, string connection, bool rememberMe, string countryCode, string timeZone, string tenantCode,
        bool internalTenantUser, bool isCourier = false, int? courierId = null, int? accountsMode = null)
    {
        return
        [
            new Claim(ClaimTypes.Name, email),
            new Claim("UserID", userId.ToString()),
            new Claim("CurrentTenantID", currentTenantId.ToString()),
            new Claim("ContactID", contactId),
            new Claim("ClientID", clientId),
            new Claim("StaffID", staffId ?? string.Empty),
            new Claim("Connection", connection),
            new Claim("CountryCode", countryCode),
            new Claim("TimeZone", timeZone),
            new Claim("TenantCode", tenantCode),
            new Claim("RememberMe", rememberMe.ToString()),
            new Claim("Internal", internalTenantUser.ToString()),
            new Claim("IsCourier", isCourier.ToString()),
            new Claim("CourierID", courierId?.ToString() ?? string.Empty),
            new Claim("AccountsMode", accountsMode?.ToString() ?? "1")
        ];
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

    public async Task<IActionResult> Logout()
    {
        // Clear the existing external cookie
        await HttpContext.SignOutAsync("Identity.Application");

        HttpContext.Session.Clear();

        return RedirectToAction("login", "Account");
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCurrentTenant([FromBody] TenantUpdateModel model)
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

        //Changed Tenant - switch connection
        var connectionString = masterUser.CurrentTenant.Dbconnection;
        Log.Debug("Changed Current Tenant. Setting new connection: {ConnectionString}", connectionString);
        var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? string.Empty;
        if (string.IsNullOrEmpty(credentials))
        {
            throw new InvalidOperationException(
                "Could not find a environment variable string named 'SQLCredentials'.");
        }

        connectionStringManager.SetConnectionString(connectionString + credentials);

        // Fetch AccountsMode once for use in claims generation
        var accountsMode = await despatchRepository.GetAccountsModeAsync();

        var user = await despatchRepository.FetchUserByUsername(User.Identity?.Name);

        if (user == null)
        {
            Log.Debug("Failed to authenticate Despatch User {IdentityName}. Invalid username.", User.Identity?.Name);
            return Json(new { success = false, message = "Despatch User not found" });
        }

        var rememberMe = bool.Parse(User.FindFirst("RememberMe")?.Value ?? "false");
        despatchRepository.UpdateUserAccessed(user.UcctId, rememberMe);
        Log.Debug("About to write Claim details. ContactID: {ToString}", user.UcctId.ToString());
        Log.Debug("About to write Claim details. Connection: {CurrentTenantDbconnection}",
            masterUser.CurrentTenant.Dbconnection);

        var claims = GenerateClaims(
            User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty,
            masterUser.UserId,
            model.TenantId,
            user.UcctId.ToString(),
            user.UcctClientId.ToString(),
            user.StaffId?.ToString() ?? string.Empty,
            masterUser.CurrentTenant.Dbconnection,
            rememberMe,
            masterUser.CurrentTenant.CountryCode,
            masterUser.CurrentTenant.TimeZone,
            masterUser.CurrentTenant.Code ?? string.Empty,
            user.UcctClient.UcclInternal,
            masterUser.IsCourier ?? false,
            accountsMode
        );

        await SignInUserAsync(claims, rememberMe);

        return Json(new { success = true });
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


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateApiKey()
    {
        try
        {
            var email = HttpContext.User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value;
            if (email == null)
            {
                Log.Debug("Failed to generate API key: email is not found");
                return Json(new { success = false, message = "Failed to generate API key" });
            }

            var uid = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "UserID")?.Value;
            if (uid == null)
            {
                Log.Debug("Failed to generate API key: UserID is not found");
                return Json(new { success = false, message = "Failed to generate API key" });
            }

            var clientId = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ClientID")?.Value;
            if (clientId == null)
            {
                Log.Debug("Failed to generate API key: ClientID is not found");
                return Json(new { success = false, message = "Failed to generate API key" });
            }

            var tenantId = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CurrentTenantID")?.Value;
            if (tenantId == null)
            {
                Log.Debug("Failed to generate API key: CurrentTenantID is not found");
                return Json(new { success = false, message = "Failed to generate API key" });
            }

            var connection = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Connection")?.Value;
            if (connection == null)
            {
                Log.Debug("Failed to generate API key: Connection is not found");
                return Json(new { success = false, message = "Failed to generate API key" });
            }

            var timeZone = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "TimeZone")?.Value;
            if (timeZone == null)
            {
                Log.Debug("Failed to generate API key: TimeZone is not found");
                return Json(new { success = false, message = "Failed to generate API key" });
            }

            var contactId = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "ContactID")?.Value;
            if (contactId == null)
            {
                Log.Debug("Failed to generate API key: ContactID is not found");
                return Json(new { success = false, message = "Failed to generate API key" });
            }

            var userId = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "UserID")?.Value;
            if (userId == null)
            {
                Log.Debug("Failed to generate API key: UserID is not found");
                return Json(new { success = false, message = "Failed to generate API key" });
            }

            var tenantCode = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "TenantCode")?.Value;
            if (tenantCode == null)
            {
                Log.Debug("Failed to generate API key: tenantCode is not found");
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
        var encryptedClaims = EncryptClaims(sensitiveClaims, Environment.GetEnvironmentVariable("ClaimsKey"));
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

    public async Task<ActionResult> Settings()
    {
        var data = new List<TenantUserSettingViewModel>();
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

        var us = await authenticationRepository.GetUserSettings(masterUser.CurrentTenant.TenantId, masterUser.UserId);
        data = us.ToList();


        var other1 = new TenantUserSettingViewModel
        {
            Id = 2,
            Name = "Test Setting",
            Value = "Testing Settings"
        };

        data.Add(other1);
        return View(data);
    }
}