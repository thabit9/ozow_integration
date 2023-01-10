using System.Reflection.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Http;
using Ozow_Integration.DataAccess;
using Ozow_Integration.Models;

namespace Ozow_Integration.Ozow
{
    public class Payment: IPayment
    { 
        private OzowContext _db;
        public Payment()
        {
            _db = new OzowContext();
        }

        #region Utilities
        /** Encode dictionary to Url string */
        public string ToUrlEncodedString(Dictionary<string, string> request)
        {
            StringBuilder builder = new StringBuilder();

            foreach (string key in request.Keys)
            {
                builder.Append("&");
                builder.Append(key);
                builder.Append("=");
                builder.Append(HttpUtility.UrlEncode(request[key]));
            }

            string result = builder.ToString().TrimStart('&');

            return result;
        }

        /** Concatinate dictionary values */
        public string ToConcatString(Dictionary<string, string> request)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string key in request.Keys)
            {
                builder.Append(HttpUtility.UrlEncode(request[key]));
            }
            string result = builder.ToString().TrimStart();
            return result;
        }

        /** Convert query string to dictionary */
        public Dictionary<string, string> ToDictionary(string response)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();

            string[] valuePairs = response.Split('&');
            foreach (string valuePair in valuePairs)
            {
                string[] values = valuePair.Split('=');
                result.Add(values[0], HttpUtility.UrlDecode(values[1]));
            }

            return result;
        }
        #endregion Utility

        #region Transactions 
        public bool AddTransaction(Dictionary<string, string> request, string transactionId)
        {
            try
            {
                //var user = User.FindFirst(ClaimTypes.Name); 
                Transaction transaction = new Transaction
                {
                    CREATED_DATE = DateTime.Now,
                    TRANSACTION_ID = transactionId,
                    TRANSACTION_REFERENCE = request["TransactionReference"],
                    AMOUNT = decimal.Parse(request["Amount"]),
                    TRANSACTION_STATUS = request["status"], 
                    TRANSACTION_MESSAGE = request["StatusMessage"],

                    OPTIONAL1 = request["Optional1"], 
                    OPTIONAL2 = request["Optional2"], 
                    OPTIONAL3 = request["Optional3"], 
                    OPTIONAL4 = request["Optional4"], 
                    IS_TEST = IsTestEnvironment(request["IsTest"]),
                    HASH = request["Hash"],
                    //CUSTOMER_EMAIL_ADDRESS = user.ToString()
                };
                _db.Transactions.Add(transaction);
                _db.SaveChanges();
                return true;
            } catch (Exception)
            {
                // log somewhere
                // at least we tried
                return false;
            }
            
        }
        public bool IsTestEnvironment(string isTest)
        {
            if(isTest == "true")
            {
                return true;
            }
            else{
                return false;
            }
        }
        // get transaction using pay request Id
        public Transaction GetTransaction(string transactionId)
        {
            Transaction transaction = _db.Transactions.FirstOrDefault(p => p.TRANSACTION_ID == transactionId);
            if (transaction == null)
            {
                return new Transaction();
            }

            return transaction;
        }

        public bool UpdateTransaction(Dictionary<string, string> request, string transactionId)
        {
            bool IsUpdated = false;

            Transaction transaction = GetTransaction(transactionId);
            if (transaction == null)
                return IsUpdated;
            
            transaction.PAYMENT_DATE = DateTime.Now;
            transaction.MERCHANT_CODE = request["MerchantCode"];
            transaction.SITE_CODE = request["SiteCode"];
            transaction.TRANSACTION_REFERENCE = request["TransactionReference"];
            transaction.CURRENCY_CODE = request["CurrencyCode"];
            transaction.AMOUNT = decimal.Parse(request["Amount"]);
            transaction.TRANSACTION_STATUS = request["status"];
            transaction.TRANSACTION_MESSAGE = request["StatusMessage"];
            try
            {
                _db.SaveChanges();
                IsUpdated = true;
            } catch (Exception)
            {
                // Oh well, log it
            }
            return IsUpdated;
        }

        /** Get the authenticated user
        public Customer GetAuthenticatedUser()
        {
            if (!HttpContext.Current.User.Identity.IsAuthenticated)
                return new ApplicationUser(); // empty user

            ApplicationUser user = new ApplicationUser();
            string email = HttpContext.Current.User.Identity.Name;
            user = _db.Users.FirstOrDefault(x => x.Email.Equals(email));
            return user;
        }*/
        #endregion Transaction

        #region MD5 Hashing
        // Adapted from
        // https://msdn.microsoft.com/en-us/library/system.security.cryptography.md5(v=vs.110).aspx

        public string GetMd5Hash(Dictionary<string, string> data, string encryptionKey)
        {
            using (MD5 md5Hash = MD5.Create())
            {
                StringBuilder input = new StringBuilder();
                foreach (string value in data.Values)
                {
                    input.Append(value);
                }

                input.Append(encryptionKey);

                byte[] hash = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input.ToString()));

                StringBuilder sBuilder = new StringBuilder();

                for (int i = 0; i < hash.Length; i++)
                {
                    sBuilder.Append(hash[i].ToString("x2"));
                }
                return sBuilder.ToString();
            }
        }

        public bool VerifyMd5Hash(Dictionary<string, string> data, string encryptionKey, string hash)
        {
            Dictionary<string, string> hashDict = new Dictionary<string, string>();

            foreach (string key in data.Keys)
            {
                if (key != "CHECKSUM")
                {
                    hashDict.Add(key, data[key]);
                }
            }

            string hashOfInput = GetMd5Hash(hashDict, encryptionKey);

            StringComparer comparer = StringComparer.OrdinalIgnoreCase;

            if (0 == comparer.Compare(hashOfInput, hash))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        #endregion MD5 Hash

        #region SHA512 Hashing
        public string SHA512(string input)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input);
            using (System.Security.Cryptography.SHA512 hash = System.Security.Cryptography.SHA512.Create())
            {
                byte[] hashedInputBytes = hash.ComputeHash(bytes);

                // Convert to text
                // StringBuilder Capacity is 128, because 512 bits / 8 bits in byte * 2 symbols for byte 
                StringBuilder hashedInputStringBuilder = new System.Text.StringBuilder(128);
                /*
                 foreach (int b in hashedInputBytes)
                    hashedInputStringBuilder.Append(b.ToString("X2"));
                */
                for (int i = 0; i < hashedInputBytes.Length; i++)
                {
                    hashedInputStringBuilder.Append(hashedInputBytes[i].ToString("X2"));
                }
                return hashedInputStringBuilder.ToString();
            }

        }
        
        public bool VerifySHA512Hash(Dictionary<string, string> data, string privateKey, string hash)
        {
            Dictionary<string, string> hashDict = new Dictionary<string, string>();

            foreach (string key in data.Keys)
            {
                if (key != "HASHCHECK")
                {
                    hashDict.Add(key, data[key]);
                }
            }

            string PostData = ToConcatString(hashDict);
            string HashString = PostData + privateKey;
            string LowerHashString = HashString.ToLower();
            string hashOfInput = SHA512(LowerHashString);

            StringComparer comparer = StringComparer.OrdinalIgnoreCase;

            if (0 == comparer.Compare(hashOfInput, hash))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        #endregion 
    }
}