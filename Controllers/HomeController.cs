using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExcelDataReader;
using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace SMSsender.Controllers
{
    public class HomeController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<HomeController> _logger;
        static int successCount = 0, failCount = 0;

        public HomeController(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<HomeController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult SendSmsFromCsv()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendSmsFromCsv(IFormFile file, int nameIndex,
    int userIdIndex,
    int passwordIndex,
    int phoneIndex)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Message"] = "Please upload a valid CSV file.";
                _logger.LogWarning("CSV file is null or empty.");
                return RedirectToAction("SendSmsFromCsv");
            }

            List<string[]> csvData;
            try
            {
                using (var stream = file.OpenReadStream())
                {
                    csvData = ReadExcel(stream);
                }


                if (csvData.Count == 0)
                {
                    TempData["Message"] = "The uploaded file is empty or invalid.";
                    _logger.LogWarning("CSV file read successfully but contains no valid data.");
                    return RedirectToAction("SendSmsFromCsv");
                }
            }
            catch (Exception ex)
            {
                TempData["Message"] = $"Error reading CSV file: {ex.Message}";
                _logger.LogError(ex, "Exception while reading CSV file.");
                return RedirectToAction("SendSmsFromCsv");
            }

            string apiUrl = _configuration["SmsSettings:ApiUrl"] ;
          
            string userName = _configuration["SmsSettings:UserName"];
            string password = _configuration["SmsSettings:Password"];

            
            nameIndex -= 1;
            userIdIndex -= 1;
            passwordIndex -= 1;
            phoneIndex -= 1;

            foreach (var record in csvData)
            {
                
                int maxIndex = Math.Max(Math.Max(nameIndex, userIdIndex), Math.Max(passwordIndex, phoneIndex));
                if (record.Length <= maxIndex)
                {
                    _logger.LogWarning("Skipping row: expected at least {Required} columns but got {Actual}.", maxIndex + 1, record.Length);
                    continue;
                }

                string recipientPhoneNumber = record[phoneIndex].Trim();
                string userId = record[userIdIndex].Trim();
                string passwordFromCsv = record[passwordIndex].Trim();
                string name = record[nameIndex].Trim();
                string uniqueID = GenerateUniqueID();


                userId = userId.PadLeft(4, '0');
        
                recipientPhoneNumber = recipientPhoneNumber.Trim();

              
                if (recipientPhoneNumber.StartsWith("9"))
                {
                    recipientPhoneNumber = "0" + recipientPhoneNumber;
                }


                string message = $"Dear {name}\r\n your User Access on ERP system created successfully.\r\nEmployeeID: {userId} \r\nPassword: {passwordFromCsv} \r\nUrl: http://erp.tsedeybank.com.et/  \r\nTsedey Bank S.C";

              

                _logger.LogInformation("Sending SMS to {PhoneNumber} with message: {Message}", recipientPhoneNumber, message);

                string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");

               
               


               
                await SendSms(apiUrl, message, timestamp, userName, password, recipientPhoneNumber);
               

            }

            TempData["Message"] = $"SMS processing completed: {successCount} successful, {failCount} failed.";
            _logger.LogInformation("SMS processing finished: {SuccessCount} success, {FailCount} failed.", successCount, failCount);

            return RedirectToAction("SendSmsFromCsv");
        }

     


        private List<string[]> ReadExcel(Stream fileStream)
        {
            var excelData = new List<string[]>();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            using (var reader = ExcelReaderFactory.CreateReader(fileStream))
            {
                do
                {
                    while (reader.Read())
                    {
                        var row = new string[reader.FieldCount];
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[i] = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
                        }

                        if (!string.IsNullOrWhiteSpace(row[0]))
                        {
                            excelData.Add(row);
                        }
                    }
                } while (reader.NextResult());
            }

            _logger.LogInformation("Read {RowCount} rows from Excel file.", excelData.Count);
            return excelData;
        }


        private string GenerateUniqueID()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 16);
        }

        private async Task SendSms( string apiUrl, string message, string timestamp, string userName, string password, string recipientPhoneNumber) {
            var httpClient = _httpClientFactory.CreateClient();
            var payload = new
            {
                timestamp = timestamp,
                phoneNumber = recipientPhoneNumber,
                userName = userName,
                password = password,
                message = message
            };


           

            var jsonContent = new StringContent(
JsonSerializer.Serialize(payload, new JsonSerializerOptions
{
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
}),
Encoding.UTF8,
"application/json"
);




            try
            {
                var response = await httpClient.PostAsync(apiUrl, jsonContent);
                string responseContent = await response.Content.ReadAsStringAsync();
                var traceId = Guid.NewGuid().ToString();

                _logger.LogInformation("TraceId: {TraceId} - Status Code: {StatusCode}. Response: {Response}",
              traceId, response.StatusCode, responseContent);


                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(responseContent);
                    var root = doc.RootElement;

                    string responseCode = root.GetProperty("responseCode").GetString();
                    string responsemessage = root.GetProperty("message").GetString();

                    if (responseCode == "0")
                    {
                        successCount++;
                              _logger.LogInformation("TraceId: {TraceId} - SMS sent successfully to {PhoneNumber}", traceId, recipientPhoneNumber);
                    }
                    else
                    {
                        failCount++;
                            _logger.LogInformation("TraceId: {TraceId} - SMS not sent   {Error}", traceId, responsemessage);

                    }
                }
                else
                {
                    failCount++;
                    _logger.LogWarning("Failed to send SMS to {PhoneNumber}. Status: {StatusCode}", recipientPhoneNumber, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                failCount++;
                _logger.LogError(ex, "Exception while sending SMS to {PhoneNumber}", recipientPhoneNumber);
            }

        }
    }
}
