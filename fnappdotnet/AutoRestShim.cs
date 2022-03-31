using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace OpenApi.Converter {
    /// <summary>
    /// Generates a Code Library from a Swagger Definition
    /// </summary>
    /// <remarks>
    /// This function is triggered by an HTTP POST request with the Swagger Definition as the body.
    /// The x-GeneratedLanguage header controls the generated output, and this will default to csharp if not specified.
    /// </remarks>
    public static class AutoRestShim
    {
        [FunctionName("CodeGen")]
        public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");     
            string autoRESTInstallPath = Path.Join(Environment.GetEnvironmentVariable("HOME"), Environment.GetEnvironmentVariable("AutoRESTPath"));

            // Get the OpenAPI Definition, the OpenAPI Definition is passed in as a JSON string in the post body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string fileGuid = Guid.NewGuid().ToString();
            string jsonOpenAPITempFileName = fileGuid + "-run.json";
            string autoRESTTempOutputFolder = Path.Join(Path.GetTempPath(), fileGuid, "\\code");
            string autoRESTTempOutputRootFolder = Path.Join(Path.GetTempPath(), fileGuid);
            
            // Write the Library to a zip file in the temp folder
            Directory.CreateDirectory(autoRESTTempOutputFolder);
            string jsonOpenAPITempFilePath = Path.Combine(Path.GetTempPath(), jsonOpenAPITempFileName);
            File.WriteAllText(jsonOpenAPITempFilePath, requestBody);

            string generateLanguage = req.Headers["x-generate-sdk"][0] ?? "csharp";
            string zipTempFileName = fileGuid + ".zip";

            // Validate the generateLanguage matches one of the supported languages
            switch(generateLanguage.ToLower())
            {
                case "csharp":
                    break;
                case "go":
                    break;
                case "java":
                    break;
                case "python":
                    break;
                case "typescript":
                    break;
                default:
                    return new BadRequestObjectResult("Unsupported language: " + generateLanguage);
            };

            // Run the AutoRest Generator
            string zipTempFilePath = Path.Combine(Path.GetTempPath(), zipTempFileName);
            Process process = new Process() { StartInfo = new ProcessStartInfo() {
                    FileName = "node",
                    Arguments = $"{autoRESTInstallPath} --input-file={jsonOpenAPITempFilePath} --output-folder={autoRESTTempOutputFolder} --{generateLanguage} ",
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,CreateNoWindow = true
            }};
            
            try {
                // Start the process
                process.Start();
                // Read the standard output
                process.WaitForExit();
                if (process.ExitCode != 0) {
                    return new BadRequestObjectResult(process.StandardError.ReadToEnd());
                }
                // Zip the output folder
                ZipFile.CreateFromDirectory(autoRESTTempOutputRootFolder, zipTempFilePath);
                // Return the zip file
                log.LogInformation($"Created zip file {zipTempFilePath}");
                return new FileStreamResult(new FileStream(zipTempFilePath, FileMode.Open), "application/zip");
            }
            catch (Exception ex) {
                // Log the exception
                return new BadRequestObjectResult(ex.Message);
            }
            finally {
                // Clean up the temp files
                File.Delete(jsonOpenAPITempFilePath);
                Directory.Delete(autoRESTTempOutputFolder, true);
                // File.Delete(zipTempFilePath);
            }
        }
    }
}
