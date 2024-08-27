using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Serilog;
using UrgentHub.Repositories;
using UrgentHub.Shared;
using UrgentMVC.Models;
using Microsoft.Extensions.DependencyInjection;
using UrgentHub.ViewModels;

namespace UrgentHub.Controllers
{
    public class AccountController(IConnectionStringManager connectionStringManager, Repository despatchRepository, AuthenticationRepository authenticationRepository, IServiceProvider serviceProvider) : Controller
    {
        // GET: /Account/Login
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.IsValid = true;
            return View();
        }



        //
        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
        {
            ViewBag.IsValid = false;
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var masterUser = await authenticationRepository.GetUserByEmail(model.Email);

            if (masterUser == null)
            {
                Log.Debug($"Failed to find user {model.Email}");
                return View(model);
            }

            if (masterUser.CurrentTenant == null)
            {
                Log.Debug($"Current Tenant Not Set for user {model.Email}");
                return View(model);
            }

            var salted = masterUser.Salt;
            var userPassword = masterUser.Password;


            var hashedPassword = PasswordHelper.HashPassword(model.Password, salted);

            if (hashedPassword != userPassword)
            {
                Log.Debug($"Failed to authenticate user {model.Email}. Invalid password.");
                return View(model);
            }

            var connectionString = masterUser.CurrentTenant.Dbconnection;
            var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? "";
            if (string.IsNullOrEmpty(credentials))
            {
                throw new InvalidOperationException(
                    "Could not find a environment variable string named 'SQLCredentials'.");
            }
            connectionStringManager.SetConnectionString(connectionString+credentials);
            

            var user = await despatchRepository.FetchUserByUsername(model.Email);

            if (user == null || !VerifyPassword(model.Password, user.Salt, user.Password2))
            {
                Log.Debug($"Failed to authenticate Despatch User {model.Email}. Invalid username or password.");
                return View(model);
            }

            despatchRepository.UpdateUserAccessed(user.UcctId, model.RememberMe);

            
            var claims = GenerateClaims(
                model.Email,
                masterUser.UserId,
                masterUser.CurrentTenant.TenantId,
                user.UcctId.ToString(),
                user.UcctClientId.ToString(),
                user.StaffId?.ToString() ?? "",
                masterUser.CurrentTenant.Dbconnection,
                model.RememberMe
            );

            await SignInUserAsync(claims, model.RememberMe);

            return RedirectToAction("Index", "Home");
            

        }

        private List<Claim> GenerateClaims(string email, int userId, int currentTenantId, string contactId, string clientId, string staffId, string connection, bool rememberMe)
        {
            return new List<Claim>
            {
                new Claim(ClaimTypes.Name, email),
                new Claim("UserID", userId.ToString()),
                new Claim("CurrentTenantID", currentTenantId.ToString()),
                new Claim("ContactID", contactId),
                new Claim("ClientID", clientId),
                new Claim("StaffID", staffId ?? ""),
                new Claim("Connection", connection),
                new Claim("RememberMe", rememberMe.ToString())
            };
        }
        
        private bool VerifyPassword(string inputPassword, string salt, string storedHash)
        {
            var inputHash = PasswordHelper.HashPassword(inputPassword, salt);
            return inputHash == storedHash;
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
            await HttpContext.SignOutAsync(
                "Identity.Application");
            return RedirectToAction("login");
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCurrentTenant([FromBody] TenantUpdateModel model)
        {
            var userId = User.FindFirst("UserID")?.Value;
            if (userId == null)
            {
                return Json(new { success = false, message = "User not found" });
            }

            if (model == null || model.TenantId == 0)
            {
                return Json(new { success = false, message = "Invalid tenant ID" });
            }
            var success = await authenticationRepository.UpdateCurrentTenantIdAsync(int.Parse(userId), model.TenantId);

            if (!success) return Json(new { success = false, message="Update database failed" });
            
            var masterUser = await authenticationRepository.GetUserById(int.Parse(userId));

            if (masterUser == null)
            {
                Log.Debug($"Failed to find master user {userId}");
                return Json(new { success = false, message = "User not found" });
            }

            if (masterUser.CurrentTenant == null)
            {
                Log.Debug($"Current Tenant Not Set for user {userId}");
                return Json(new { success = false, message = $"Current Tenant Not Set for user {userId}"});
            }
            
            //Changed Tenant - switch connection
            var connectionString = masterUser.CurrentTenant.Dbconnection;
            Log.Debug($"Changed Current Tenant. Setting new connection: {connectionString}");
            var credentials = Environment.GetEnvironmentVariable("SQLCredentials") ?? "";
            if (string.IsNullOrEmpty(credentials))
            {
                throw new InvalidOperationException(
                    "Could not find a environment variable string named 'SQLCredentials'.");
            }
            connectionStringManager.SetConnectionString(connectionString+credentials);
            
            var user = await despatchRepository.FetchUserByUsername(User.Identity?.Name);

            if (user == null)
            {
                Log.Debug($"Failed to authenticate Despatch User {User.Identity?.Name}. Invalid username.");
                return Json(new { success = false, message = "Despatch User not found" });
            }
            
            var rememberMe =bool.Parse(User.FindFirst("RememberMe")?.Value ?? "false");
            despatchRepository.UpdateUserAccessed(user.UcctId, rememberMe);
            Log.Debug($"About to write Claim details. ContactID: {user.UcctId.ToString()}");
            Log.Debug($"About to write Claim details. Connection: {masterUser.CurrentTenant.Dbconnection}");
            
            var claims = GenerateClaims(
                User.FindFirst(ClaimTypes.Name)?.Value ?? "",
                masterUser.UserId,
                model.TenantId,
                user.UcctId.ToString(),
                user.UcctClientId.ToString(),
                user.StaffId?.ToString() ?? "",
                masterUser.CurrentTenant.Dbconnection,
                rememberMe
            );

            await SignInUserAsync(claims, rememberMe);

            return Json(new { success = true });

        }
    }
}
