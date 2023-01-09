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
                }, new Newtonsoft.Json.JsonSerializerSettings());//JsonRequestBehavior.AllowGet);
            }

            if (!_payment.VerifyMd5Hash(results, PayGateKey, results["CHECKSUM"]))
            {
                return Json(new
                {
                    success = false,
                    message = "MD5 verification failed"
                }, new Newtonsoft.Json.JsonSerializerSettings());//JsonRequestBehavior.AllowGet);
            }

            bool IsRecorded = _payment.AddTransaction(request, results["PAY_REQUEST_ID"]);
            if (IsRecorded)
            {
                return Json(new
                {
                    success = true,
                    message = "Request completed successfully",
                    results
                }, new Newtonsoft.Json.JsonSerializerSettings());//JsonRequestBehavior.AllowGet);
            }
            return Json(new
            {
                success = false,
                message = "Failed to record a transaction"
            }, new Newtonsoft.Json.JsonSerializerSettings());//JsonRequestBehavior.AllowGet);
        }

        // This is your return url from Paygate
        // This will run when you complete payment
        [HttpPost]
        public async Task<ActionResult> CompletePayment()
        {
            var PayGateID = _configuration["PayGate:PayGateID"];
            var PayGateKey  = _configuration["PayGate:PayGateKey"];

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
            validationSet.Add("PAYGATE_ID", PayGateID);
            validationSet.Add("PAY_REQUEST_ID", results["PAY_REQUEST_ID"]);
            validationSet.Add("TRANSACTION_STATUS", results["TRANSACTION_STATUS"]);
            validationSet.Add("REFERENCE", transaction.REFERENCE);

            if (!_payment.VerifyMd5Hash(validationSet, PayGateKey, results["CHECKSUM"]))
            {
                // checksum error
                return RedirectToAction("Failed");
            }
            /** Payment Status 
             * -2 = Unable to reconsile transaction
             * -1 = Checksum Error
             * 0 = Pending
             * 1 = Approved
             * 2 = Declined
             * 3 = Cancelled
             * 4 = User Cancelled
             */
            int paymentStatus = int.Parse(results["TRANSACTION_STATUS"]);
            if(paymentStatus == 1)
            {
                // Yey, payment approved
                // Do something useful
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