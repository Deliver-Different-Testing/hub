using System;
using System.IO;
using System.Security.Cryptography;
using Hub.Models;

namespace Hub.Shared
{
    public static class PasswordHelper
    {
        /// <summary>
        /// call to create or check/verify a hashed password using the unique salt value
        /// </summary>
        /// <param name="password"></param>
        /// <param name="salt"></param>
        /// <returns></returns>
        public static string HashPasswordLegacy(string password, string salt)
        {
            var k2 = new Rfc2898DeriveBytes(password, System.Text.Encoding.UTF8.GetBytes(salt + salt));
            byte[] hashBytes = k2.GetBytes(64); // 64 bytes = 512 bits
            string result = BitConverter.ToString(hashBytes).Replace("-", "");
            return result;
        }

        /// <summary>
        /// Call to correctly set the unique salt value and hash the new password for a user
        /// </summary>
        /// <param name="password"></param>
        /// <returns></returns>
        public static SaltHashed SaltHashNewPasswordLegacy(string password)
        {
            var random = new Random();  
            var salt = random.Next(10000, 99999);
            var salted = salt.ToString();
            var result = new SaltHashed
            {
                Salt = salted,
                Hashed = HashPasswordLegacy(password, salted)
            };
            return result;
        }
        
        public static SaltHashed SaltHashNewPassword(string password)
        {
            var random = new Random();
            var salt = random.Next(10000, 99999);
            var salted = salt.ToString();
            var result = new SaltHashed
            {
                Salt = salted,
                Hashed = HashPassword(password, salted)
            };
            return result;
        }

        public static string HashPassword(string password, string salt)
        {
            // Using the recommended constructor with explicit iteration count parameter (default is 1000)
            var k2 = new Rfc2898DeriveBytes(
                password, 
                System.Text.Encoding.UTF8.GetBytes(salt + salt),
                10000,
                HashAlgorithmName.SHA256);
        
            var hashBytes = k2.GetBytes(64); // 64 bytes = 512 bits
            var result = BitConverter.ToString(hashBytes).Replace("-", "");
            return result;
        }


        public static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (cipherText is not { Length: > 0 })
                throw new ArgumentNullException("cipherText");
            if (Key is not { Length: > 0 })
                throw new ArgumentNullException("Key");
            if (IV is not { Length: > 0 })
                throw new ArgumentNullException("IV");

            // Declare the string used to hold
            // the decrypted text.
            string plaintext = null;

            // Create an Aes object
            // with the specified key and IV.
            using var aesAlg = Aes.Create();
            aesAlg.Key = Key;
            aesAlg.IV = IV;

            // Create a decrytor to perform the stream transform.
            var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

            // Create the streams used for decryption.
            using var msDecrypt = new MemoryStream(cipherText);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);
            // Read the decrypted bytes from the decrypting stream and place them in a string.
            plaintext = srDecrypt.ReadToEnd();

            return plaintext;
        }

        public static byte[] EncryptStringToBytes_Aes(string plainText, byte[] key, byte[] iv)
        {
            // Check arguments.
            if (plainText is not { Length: > 0 })
                throw new ArgumentNullException("plainText");
            if (key is not { Length: > 0 })
                throw new ArgumentNullException("key");
            if (iv is not { Length: > 0 })
                throw new ArgumentNullException("iv");
            // Create an Aes object
            // with the specified key and IV.
            using var aesAlg = Aes.Create();
            aesAlg.Key = key;
            aesAlg.IV = iv;

            // Create a decrytor to perform the stream transform.
            var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            // Create the streams used for encryption.
            using var msEncrypt = new MemoryStream();
            using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
            using (var swEncrypt = new StreamWriter(csEncrypt))
            {
                //Write all data to the stream.
                swEncrypt.Write(plainText);
            }
            var encrypted = msEncrypt.ToArray();

            // Return the encrypted bytes from the memory stream.
            return encrypted;
        }
    }
}
