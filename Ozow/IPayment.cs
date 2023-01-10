using System.Collections.Generic;
using Ozow_Integration.Models;

namespace Ozow_Integration.Ozow
{
    public interface IPayment
    {
        
        string ToUrlEncodedString(Dictionary<string, string> request);
        string ToConcatString(Dictionary<string, string> request);
        Dictionary<string, string> ToDictionary(string response);
        bool AddTransaction(Dictionary<string, string> request, string payRequestId);
        bool UpdateTransaction(Dictionary<string, string> request, string PayrequestId);
        Transaction GetTransaction(string payRequestId);
        string GetMd5Hash(Dictionary<string, string> data, string encryptionKey);
        bool VerifyMd5Hash(Dictionary<string, string> data, string encryptionKey, string hash);
        string SHA512(string input);
        bool VerifySHA512Hash(Dictionary<string, string> data, string privateKey, string hash);
        //ApplicationUser GetAuthenticatedUser();
        //void UpdateTransactionStatus(Transaction transaction);        
    }
}