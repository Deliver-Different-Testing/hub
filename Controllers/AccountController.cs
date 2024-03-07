using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Configuration;
using System;
using System.Web;
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
        public ActionResult Login(LoginViewModel model, string returnUrl)
        {
            ViewBag.IsValid = false;
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var users = _repo.FetchUsersByUsername(model.Email);
            var found = false;
            var contactId = "";

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
                    _repo.UpdateUserAccessed(user.UcctId, model.RememberMe);
                }
                break;

            }

            if (!found)
            {
                return View(model);
            }

            ViewBag.IsValid = true;


            
            var redirectData = Encrypt(contactId + "|" + DateTime.Now);
            redirectData = HttpUtility.UrlEncode(redirectData);
            var ret = redirectData;

            return RedirectToAction("Index", "Home",  new { login = ret });

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
    }
}
