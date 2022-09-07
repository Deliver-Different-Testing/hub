using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UrgentHub.Repositories;
using UrgentHub.Models;

namespace UrgentHub.Controllers
{
    public class HomeController : Controller
    {
        private Repository _repo;
        private IConfiguration _configuration;

        public HomeController(Repository repo, IConfiguration configuration)
        {
            _repo = repo;
            _configuration = configuration;

        }

        public IActionResult Index([FromQuery] string login)
        {
            if (!string.IsNullOrEmpty(login))
            {
                var key = Convert.FromBase64String("8wzkFlOvNB8+7UgmX0bSyFPHxjLNjmaGhlUoSKkJ2Kc=");
                var iv = Convert.FromBase64String("iE1+VfQbXWBkCalh+iV85Q==");

                if (login.Contains(" ")) login = login.Replace(" ", "+");

                if (login.Contains("%2f")) login = login.Replace("%2f", "/");

                var loginString = DecryptStringFromBytes_Aes(Convert.FromBase64String(login), key, iv);

                if (loginString == null || loginString.LastIndexOf("|") <= 0) return Redirect(_configuration["PublicPath"]);

#if DEBUG
                Console.WriteLine("Mode=Debug");
                var provider = CultureInfo.InvariantCulture;
                var timeStamp = DateTime.Parse(loginString.Substring(loginString.LastIndexOf("|") + 1).Replace(".", ""));

                var diffInTimes = DateTime.Now.Subtract(timeStamp);
                var diffInMinutes = diffInTimes.TotalMinutes;
                Console.WriteLine("Diff in Minutes = " + diffInMinutes);
#else
                var provider = CultureInfo.InvariantCulture;
                var timeStamp = DateTime.Parse(loginString.Substring(loginString.LastIndexOf("|") + 1).Replace(".", ""));

                var diffInTimes = DateTime.Now.Subtract(timeStamp);
                var diffInMinutes = diffInTimes.TotalMinutes;

                if (Math.Abs(diffInMinutes) > 480)
                {
                    //Return "ERROR: Data is too old
                    return Redirect(_configuration["PublicPath"]);
                }
#endif


                var contactId = loginString.Substring(0, loginString.LastIndexOf("|")); //Always receiving contactId

                if (string.IsNullOrEmpty(contactId)) return View();

                var contactDetail = _repo.GetContact(int.Parse(contactId));
                var clientDetail = _repo.GetClient(contactDetail.ClientId);
                var internetPermissions = _repo.GetDespatchWebInternetPermissions(int.Parse(contactId));

                //if (!string.IsNullOrEmpty(jobNumber))
                //{
                //    var bj = _repo.BulkJobByJobNumber(jobNumber);
                //    ViewBag.JobNumber = jobNumber;
                //    ViewBag.BookDate = bj.BookDate.ToString("yyyy-MM-dd");
                //    ViewBag.BulkRegionID = bj.BulkRegionID;
                //    ViewBag.BulkJobID = bj.BulkJobID;
                //    ViewBag.RunID = bj.RunID;
                //}

                ViewBag.ContactID = int.Parse(contactId);
                ViewBag.ContactName = contactDetail.FirstName;
                ViewBag.ContactFullName = contactDetail.FirstName + " " + contactDetail.SurName;
                ViewBag.ContactEmail = contactDetail.UserName;
                ViewBag.ContactCreated = (Int32)(contactDetail.Created.ToUniversalTime().Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                ViewBag.GreetingString = GetGreetingString();
                ViewBag.DespatchWebPermission = GetPermission(internetPermissions, 12);
                ViewBag.BookJobPermission = GetPermission(internetPermissions, 2);

                ViewBag.ClientName = clientDetail.Name;
                ViewBag.ClientInternal = clientDetail.Internal;
                ViewBag.ClientID = contactDetail.ClientId;
                ViewBag.ClientCreated = (Int32) (clientDetail.Created.ToUniversalTime().Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

                return View();
            }
            else
            {
                return Redirect(_configuration["PublicPath"]);
            }
        }

        public string GetGreetingString()
        {
            string[] greetings = new string[] { "Hi", "Hello", "Welcome", "Greetings", "G'day", "Hey", "Good to see you,", "How are you", "Hope it's swell,", "How's it going", "What's good", "Howdy", "Kia ora", "Tēnā koe",
            "The world is yours,", "Hi", "Hello", "Hey", "All the best,", "Enjoy,", "Seize the day,", "Kia ora" };
            Random random = new Random();
            return greetings[random.Next(greetings.Length)];
        }

        public bool GetPermission(List<InternetPermission> internetPermissions, int internetPermissionId)
        {
            foreach (InternetPermission i in internetPermissions)
            {
                if (i.InternetPermissionId == internetPermissionId)
                {
                    return true;
                }
            }

            return false;
        }

        protected string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException("cipherText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");

            // Declare the string used to hold
            // the decrypted text.
            string plaintext = null;

            // Create an Aes object
            // with the specified key and IV.
            using (var aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                // Create a decrytor to perform the stream transform.
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for decryption.
                using (var msDecrypt = new MemoryStream(cipherText))
                {
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (var srDecrypt = new StreamReader(csDecrypt))
                        {
                            // Read the decrypted bytes from the decrypting stream and place them in a string.
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }
            return plaintext;
        }

        public static string EncryptOutgoingToString(int userID, string page)
        {
            if (!string.IsNullOrEmpty(userID.ToString()))
            {
                var key = Convert.FromBase64String("8wzkFlOvNB8+7UgmX0bSyFPHxjLNjmaGhlUoSKkJ2Kc=");
                var iv = Convert.FromBase64String("iE1+VfQbXWBkCalh+iV85Q==");

                var id = userID.ToString();
                var data = id + "|" + DateTime.Now;

                var encData = EncryptStringToBytes_Aes(data, key, iv);

                var encryptedString = HttpUtility.UrlEncode(Convert.ToBase64String(encData));

                if (page == "bulkImport")
                    encryptedString = encryptedString.Replace("%2f", "%252f");

                return encryptedString;
            } else {
                return "";
            }
        }

        public static byte[] EncryptStringToBytes_Aes(string plainText, byte[] key, byte[] iv)
        {
            // Check arguments.
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (key == null || key.Length <= 0)
                throw new ArgumentNullException("key");
            if (iv == null || iv.Length <= 0)
                throw new ArgumentNullException("iv");
            byte[] encrypted;
            // Create an Aes object
            // with the specified key and IV.
            using (var aesAlg = Aes.Create())
            {
                aesAlg.Key = key;
                aesAlg.IV = iv;

                // Create a decrytor to perform the stream transform.
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for encryption.
                using (var msEncrypt = new MemoryStream())
                {
                    using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (var swEncrypt = new StreamWriter(csEncrypt))
                        {
                            //Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return the encrypted bytes from the memory stream.
            return encrypted;
        }
    }
}

