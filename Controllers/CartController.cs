using System.Security.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Ozow_Integration.Models;
using Ozow_Integration.DataAccess;
using System.Security.Claims;
using Ozow_Integration.Ozow;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Configuration;
using System.Text;
using System.Web;

namespace Ozow_Integration.Controllers
{
    public class CartController : Controller
    {
        private IPayment _payment = new Payment();

        //readonly static string PayGateID = _configuration["appSettings:PayGateID"];
        //readonly static string PayGateKey  = _configuration["appSettings:PayGateKey"];
        private IConfiguration _configuration { get; set; }
        public CartController(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        
        public ActionResult Index()
        {
            return View();
        }

        public async Task<JsonResult> GetRequest()
        {
            //var user = User.FindFirst(ClaimTypes.Name);    
            
            var SiteCode = _configuration["OZOW:SiteCode"];
            var PrivateKey  = _configuration["OZOW:PrivateKey"];
            var APIKey  = _configuration["OZOW:APIKey"];

            HttpClient http = new HttpClient();
            Dictionary<string, string> request = new Dictionary<string, string>();
            string paymentAmount = (50 * 100).ToString("00"); // amount int cents e.i 50 rands is 5000 cents
            
            request.Add("SITE_CODE", SiteCode);
            request.Add("COUNTRY_CODE", "ZAR");
            request.Add("CURRENCY_CODE", "ZAR");
            request.Add("TOTAL_AMOUNT", paymentAmount);
            request.Add("TRANSACTION_REFERENCE", "#45846"); // Payment ref e.g ORDER NUMBER
            request.Add("BANK_REFERENCE", "ABC#45846"); // Payment ref e.g ORDER NUMBER
            // Optional fields
            request.Add("OPTIONAL1", "thabit9@gmail.com"); // Customer Email
            request.Add("OPTIONAL2", "#25756"); // CustID
            request.Add("OPTIONAL3", "#45846"); // BasketID
            request.Add("OPTIONAL4", "#45846"); // OrderID
            request.Add("CUSTOMER", "Thabi Tabana"); // Customer Name

            // OZOW now needs a real/non-localhost url as the success_url, error_url, cancel_url, and notified_url
            // TODO: Here you can add any website url to test, but bear in mind that it will return to this website once payment is completes
            // Important Urls
            request.Add("CANCEL_URL", $"{Request.Scheme}://{Request.Host}/cart/complete");
            request.Add("ERROR_URL", $"{Request.Scheme}://{Request.Host}/cart/complete");
            request.Add("SUCCESS_URL", $"{Request.Scheme}://{Request.Host}/cart/complete");
            request.Add("NOTIFY_URL", $"{Request.Scheme}://{Request.Host}/cart/complete");
            request.Add("IS_TEST", "true");

            // Create CHECKSUM Field
            // Concatenate the post variables (excluding HashCheck and Token) in the order they appear in the post variables table
            string PostData = _payment.ToConcatString(request);
            // Append your private key to the concatenated string. Your private key can be found in merchant details section of the merchant admin site.
            string HashString = PostData + PrivateKey;
            // Convert the concatenated string to lowercase.
            string LowerHashString = HashString.ToLower();
            // Generate a SHA512 hash of the lowercase concatenated string.
            string HashCheck = _payment.SHA512(LowerHashString);
            // add to the dictionary
            request.Add("HASHCHECK", HashCheck);

            string requestString = _payment.ToUrlEncodedString(request);
            StringContent content = new StringContent(requestString, Encoding.UTF8, "application/x-www-form-urlencoded");
            HttpResponseMessage response = await http.PostAsync("https://pay.ozow.com/", content);
            // if the request did not succeed, this line will make the program crash
            response.EnsureSuccessStatusCode();

            string responseContent = await response.Content.ReadAsStringAsync();

            Dictionary<string, string> results = _payment.ToDictionary(responseContent);

            if (results.Keys.Contains("ERROR"))
            {
                return Json(new
                {
                    success = false,
                    message = "An error occured while initiating your request"
                }, new Newtonsoft.Json.JsonSerializerSettings());
            }

            if (!_payment.VerifySHA512Hash(results, PrivateKey, results["HASHCHECK"]))
            {
                return Json(new
                {
                    success = false,
                    message = "SHA512 verification failed"
                }, new Newtonsoft.Json.JsonSerializerSettings());
            }

            bool IsRecorded = _payment.AddTransaction(request, results["TRANSACTION_ID"]);
            if (IsRecorded)
            {
                return Json(new
                {
                    success = true,
                    message = "Request completed successfully",
                    results
                }, new Newtonsoft.Json.JsonSerializerSettings());
            }
            return Json(new
            {
                success = false,
                message = "Failed to record a transaction"
            }, new Newtonsoft.Json.JsonSerializerSettings());
        }

        // This is your return url from Paygate
        // This will run when you complete payment
        [HttpPost]
        public async Task<ActionResult> CompletePayment()
        {
            var SiteCode = _configuration["OZOW:SiteCode"];
            var PrivateKey  = _configuration["OZOW:PrivateKey"];
            var APIKey  = _configuration["OZOW:APIKey"];

            //string responseContent = Request.Params.ToString();
            string responseContent = Request.QueryString.ToString();
            Dictionary<string, string> results = _payment.ToDictionary(responseContent);

            Transaction transaction = _payment.GetTransaction(results["PAY_REQUEST_ID"]);

            if (transaction == null)
            {
                // Unable to reconsile transaction
                return RedirectToAction("Failed");
            }

            // Reorder attributes for MD5 check
            Dictionary<string, string> validationSet = new Dictionary<string, string>();
            validationSet.Add("SiteCode", SiteCode);
            validationSet.Add("TransactionId", results["TransactionId"]);
            validationSet.Add("TransactionReference", results["TransactionReference"]);
            validationSet.Add("Amount", results["Amount"]);
            validationSet.Add("Status", results["Status"]);
            // Optional fields
            validationSet.Add("Optional1", results["Optional1"]); // Customer Email
            validationSet.Add("Optional1", results["Optional2"]); // CustID
            validationSet.Add("Optional3", results["Optional3"]); // BasketID
            validationSet.Add("Optional4", results["Optional3"]); // OrderID
            validationSet.Add("Customer", results["Customer"]); // Customer Name
            validationSet.Add("CurrencyCode", results["CurrencyCode"]);
            validationSet.Add("IsTest", results["IsTest"]);
            validationSet.Add("StatusMessage", results["StatusMessage"]);
            validationSet.Add("Hash", results["Hash"]);

            if (!_payment.VerifySHA512Hash(validationSet, PrivateKey, results["HASHCHECK"]))
            {
                // checksum error
                return RedirectToAction("Failed");
            }
            /** Payment Status 
             * "Complete"
             * "Cancelled"
             * "Error"
             * "Abandoned"
             * "PendingInvestigation"
             */
            string strResponse = ""; 
            string paymentStatus = results["Status"].ToString();
            switch (paymentStatus)
            {
                case "Complete":
                    strResponse = "~Approved. Transaction Reference=" + results["Optional1"] + ": " + results["StatusMessage"];
                    break;
                case "Cancelled":
                    strResponse = "~Cancelled. Transaction Reference=" + results["Optional1"] + ": " + results["StatusMessage"];
                    break;
                case "Error":
                    strResponse = "~Error. Transaction Reference=" + results["Optional1"] + ": " + results["StatusMessage"];
                    break;
                case "Abandoned":
                    strResponse = "~Abandoned. Transaction Reference=" + results["Optional1"] + ": " + results["StatusMessage"];
                    break;
                case "PendingInvestigation":
                    strResponse = "~Pending Investigation. Transaction Reference=" + results["Optional1"] + ": " + results["StatusMessage"];
                    break;
                default:
                    strResponse = "~Cancelled. Error occured during your transaction.";
                    break;
            }
            // Query paygate transaction details
            // And update user transaction on your database
            await VerifyTransaction(responseContent, transaction.REFERENCE);
            return RedirectToAction("Complete", new { id = results["TRANSACTION_STATUS"] });
        }        
        
        private async Task VerifyTransaction(string responseContent, string Referrence)
        {
            var PayGateID = _configuration["PayGate:PayGateID"];
            var PayGateKey  = _configuration["PayGate:PayGateKey"];

            HttpClient client = new HttpClient();
            Dictionary<string, string> response = _payment.ToDictionary(responseContent);
            Dictionary<string, string> request = new Dictionary<string, string>();

            request.Add("PAYGATE_ID", PayGateID);
            request.Add("PAY_REQUEST_ID", response["PAY_REQUEST_ID"]);
            request.Add("REFERENCE", Referrence);
            request.Add("CHECKSUM", _payment.GetMd5Hash(request, PayGateKey));

            string requestString = _payment.ToUrlEncodedString(request);

            StringContent content = new StringContent(requestString, Encoding.UTF8, "application/x-www-form-urlencoded");

            // ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpResponseMessage res = await client.PostAsync("https://secure.paygate.co.za/payweb3/query.trans", content);
            res.EnsureSuccessStatusCode();

            string _responseContent = await res.Content.ReadAsStringAsync();

            Dictionary<string, string> results = _payment.ToDictionary(_responseContent);
            if (!results.Keys.Contains("ERROR"))
            {
                _payment.UpdateTransaction(results, results["PAY_REQUEST_ID"]);
            }

        }

        [Route("complete")]
        public IActionResult Complete(int? id)
        {
            string status = "Unknown";
            switch (id.ToString())
            {
                case "-2":
                    status = "Unable to reconsile transaction";
                    break;
                case "-1":
                    status = "Checksum Error. The values have been altered";
                    break;
                case "0":
                    status = "Not Done";
                    break;
                case "1":
                    status = "Approved";
                    break;
                case "2":
                    status = "Declined";
                    break;
                case "3":
                    status = "Cancelled";
                    break;
                case "4":
                    status = "User Cancelled";
                    break;
                default:
                    status = $"Unknown Status({ id })";
                    break;
            }
            TempData["Status"] = status;

            return View("Complete");
        }
    
    
    }
}