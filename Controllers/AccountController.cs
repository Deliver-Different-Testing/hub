using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Configuration;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using UrgentHub.Shared;
using UrgentMVC.Models;
using UrgentHub.Repositories;

namespace UrgentHub.Controllers
{
    public class AccountController : Controller
    {
        private readonly Repository _repo;

        public AccountController(Repository repo)
        {
            _repo = repo;
        }

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

            var users = _repo.FetchUsersByUsername(model.Email);
            var found = false;
            var contactId = "";
            var clientId = "";

            foreach (var user in users)
            {
                var salted = user?.Salt;
                var dbpw = user?.Password2;

                if (user == null)
                {
                    return View(model);
                }

                var hashedpw = PasswordHelper.HashPassword(model.Password, salted);

                if (hashedpw == dbpw)
                {
                    found = true;
                    contactId = user.UcctId.ToString();
                    clientId = clientId = user.UcctClientId.ToString(); 
                    _repo.UpdateUserAccessed(user.UcctId, model.RememberMe);
                }
                break;

            }

            if (!found)
            {
                return View(model);
            }
            else
            {
                ViewBag.IsValid = true;
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, model.Email),
                    new Claim("ContactID", contactId),
                    new Claim("ClientID", clientId)
                };

                var claimsIdentity = new ClaimsIdentity(
                    claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    AllowRefresh = true,

                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
                    // The time at which the authentication ticket expires. A 
                    // value set here overrides the ExpireTimeSpan option of 
                    // CookieAuthenticationOptions set with AddCookie.

                    IsPersistent = model.RememberMe,
                    // Whether the authentication session is persisted across 
                    // multiple requests. When used with cookies, controls
                    // whether the cookie's lifetime is absolute (matching the
                    // lifetime of the authentication ticket) or session-based.

                    //IssuedUtc = <DateTimeOffset>,
                    // The time at which the authentication ticket was issued.

                    //RedirectUri = <string>
                    // The full path or absolute URI to be used as an http 
                    // redirect response value.
                };

                await HttpContext.SignInAsync(
                    "Identity.Application",
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                
                return RedirectToAction("Index", "Home");
                

            }

            

        }

        public static string Encrypt(string decryptedString)
        {

            var key = Convert.FromBase64String("8wzkFlOvNB8+7UgmX0bSyFPHxjLNjmaGhlUoSKkJ2Kc=");
            var iv = Convert.FromBase64String("iE1+VfQbXWBkCalh+iV85Q==");




            var encryptedString = "";
            try
            {

                var encData = PasswordHelper.EncryptStringToBytes_Aes(decryptedString, key, iv);
                encryptedString = Convert.ToBase64String(encData);


            }
            catch (Exception)
            {
            }

            return encryptedString;
        }

        public static string Decrypt(string encryptedString)
        {
            var key = Convert.FromBase64String("8wzkFlOvNB8+7UgmX0bSyFPHxjLNjmaGhlUoSKkJ2Kc=");
            var iv = Convert.FromBase64String("iE1+VfQbXWBkCalh+iV85Q==");

            var decryptedString = "";
            try
            {
                decryptedString = PasswordHelper.DecryptStringFromBytes_Aes(Convert.FromBase64String(encryptedString), key, iv);
            }
            catch (Exception)
            {

            }

            return decryptedString;
        }

        public async Task<IActionResult> Logout()
        {
            // Clear the existing external cookie
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("login");
        }
    }
}
