using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Shipwreck.Phash.PresentationCore;
using Shipwreck.Phash.Bitmaps;
using Shipwreck.Phash;
using Shipwreck.Phash.Imaging;
using System.Drawing;
using Newtonsoft.Json;
using KMB_ImageComparison.PP_SOAP_Service;
using System.Net;
using System.Configuration;

namespace KMB_ImageComparison
{
    class Program
    {
        public static Field[] AssetFields;
        public static ExtendedPublicServiceClient PictureparkService = new ExtendedPublicServiceClient("Default") { };
        public static CoreInfo coreInfo;

        private static Configuration Configuration;

        static void Main(string[] args)
        {
            try
            {
                var proxy = new WebProxy(ConfigurationManager.AppSettings["address"], true);
                proxy.Credentials = new NetworkCredential(ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["password"]);
                WebRequest.DefaultWebProxy = proxy;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: Please check proxy settings in KMB_ImageComparison.exe.config");
                Console.ReadLine();
                Environment.Exit(0);
            }

            PictureparkService.InnerChannel.OperationTimeout = new TimeSpan(0, 20, 0);

            while (true)
            {
                Console.WriteLine("Choose option: ");
                Console.WriteLine("1: Run comparison");
                Console.WriteLine("2: Generate configuration-file");

                switch (Console.ReadLine())
                {
                    //run comparison
                    case "1":
                        #region run comparison
                        if (CheckConfiguration())
                        {
                            //get asset fields
                            AssetFields = PictureparkService.GetAssetFields(coreInfo);

                            //create temp folder if not already exists
                            string tempPath = Configuration.ResultPath + "\\temp";
                            if (!Directory.Exists(tempPath))
                                Directory.CreateDirectory(tempPath);

                            //create missing images folder if not already exists
                            string missingImgPath = Configuration.ResultPath + "\\missing_on_pp";
                            if (!Directory.Exists(missingImgPath))
                                Directory.CreateDirectory(missingImgPath);

                            //create missing images folder if not already exists
                            string invalidAssetPath = Configuration.ResultPath + "\\invalid_asset_on_pp";
                            if (!Directory.Exists(invalidAssetPath))
                                Directory.CreateDirectory(invalidAssetPath);

                            //create multiple standard images folder if not already exists
                            string multipleStandardImgPath = Configuration.ResultPath + "\\multiple_w11_in_pp";
                            if (!Directory.Exists(multipleStandardImgPath))
                                Directory.CreateDirectory(multipleStandardImgPath);


                            //create matching image folder if not already exists
                            string matchingFolder = Configuration.ResultPath + "\\higher_99";
                            if (!Directory.Exists(matchingFolder))
                                Directory.CreateDirectory(matchingFolder);

                            //create image folder if not already exists
                            string between_85_and_99 = Configuration.ResultPath + "\\between_85_and_99";
                            if (!Directory.Exists(between_85_and_99))
                                Directory.CreateDirectory(between_85_and_99);

                            //create image folder if not already exists
                            string between_75_and_85 = Configuration.ResultPath + "\\between_75_and_85";
                            if (!Directory.Exists(between_75_and_85))
                                Directory.CreateDirectory(between_75_and_85);

                            //create image folder if not already exists
                            string between_65_and_75 = Configuration.ResultPath + "\\between_65_and_75";
                            if (!Directory.Exists(between_65_and_75))
                                Directory.CreateDirectory(between_65_and_75);

                            //create image folder if not already exists
                            string between_55_and_65 = Configuration.ResultPath + "\\between_55_and_65";
                            if (!Directory.Exists(between_55_and_65))
                                Directory.CreateDirectory(between_55_and_65);

                            //create not matchign folder if not already exists
                            string notMatchingFolder = Configuration.ResultPath + "\\lower_55";
                            if (!Directory.Exists(notMatchingFolder))
                                Directory.CreateDirectory(notMatchingFolder);


                            //loop images in ImageFolder
                            DirectoryInfo imgDir = new DirectoryInfo(Configuration.ImagePath);

                            using (var client = new WebClient())
                            {
                                foreach (FileInfo fileToCheck in imgDir.GetFiles())
                                {
                                    coreInfo = LoginPP(PictureparkService);

                                    Console.WriteLine("Checking file: " + fileToCheck.Name);

                                    //only check jpg files, ignore others
                                    if (fileToCheck.Extension.ToLower() == ".jpg")
                                    {
                                        string strObjId = fileToCheck.Name.Replace(fileToCheck.Extension, "");
                                        int objId;
                                        if (int.TryParse(strObjId, out objId))
                                        {
                                            List<ComparisonOperation> comparisonOperations = new List<ComparisonOperation>();
                                            StringEqualOperation stringEqualOperation = new StringEqualOperation() { FieldName = "OBJID", EqualString = objId.ToString() };
                                            comparisonOperations.Add(stringEqualOperation);
                                            AndOperation searchOperations = new AndOperation() { ComparisonOperations = comparisonOperations.ToArray() };

                                            ExtendedAssetFilter filter = new ExtendedAssetFilter()
                                            {
                                                AdditionalSelectFields = new string[] { "AssetName", "OBJID", "KeinDownloadOnline", "MP_Copyright" },
                                                SearchOperation = searchOperations
                                            };

                                            AssetItemCollection collection = PictureparkService.GetAssets(coreInfo, filter);

                                            List<AssetItem> assets = collection.Assets
                                                .Where(a => a.FieldValues
                                                    .Where(fv => fv.FieldId == GetFieldIdByName(AssetFields.ToList(), "AssetName"))
                                                    .Any(fv => fv.StringValue.Substring(1, 3).ToLower() == "w11")).ToList();

                                            if (assets.Count() > 1)
                                            {
                                                //more than 1 w11 exists for this objId
                                                Console.WriteLine("More than 1 w11 exists for this objId");
                                                File.Move(fileToCheck.FullName, multipleStandardImgPath + "\\" + fileToCheck.Name);
                                            }
                                            else if (assets.Count() == 1)
                                            {
                                                AssetItem asset = assets[0];

                                                List<AssetSelection> assetSelection = new List<AssetSelection>() {
                                                    new AssetSelection()
                                                    {
                                                        AssetId = asset.AssetId,
                                                        DerivativeDefinitionId = 7
                                                    }
                                                };

                                                DownloadOptions downloadOptions = new DownloadOptions()
                                                {
                                                    UserAction = UserAction.DerivativeDownload
                                                };

                                                Download download = new Download();

                                                try
                                                {
                                                    download = PictureparkService.Download(coreInfo, assetSelection.ToArray(), downloadOptions);
                                                }
                                                catch (Exception ex)
                                                {
                                                    File.Move(fileToCheck.FullName, invalidAssetPath + "\\" + fileToCheck.Name);
                                                    Console.WriteLine("Problem with asset on picturepark");
                                                    continue;
                                                }

                                                //download asset into temp directory
                                                string downloadFilePath = tempPath + "\\" + download.DownloadFileName;
                                                client.DownloadFile(download.URL, downloadFilePath);

                                                double comparisonScore = CompareImages(fileToCheck.FullName, downloadFilePath);

                                                string folder;

                                                if (comparisonScore > 0.99)
                                                {
                                                    Console.WriteLine("Images match!");
                                                    folder = matchingFolder;
                                                }
                                                else if (comparisonScore > 0.95)
                                                {
                                                    Console.WriteLine("Images are between 0.85 and 0.99!");
                                                    folder = between_85_and_99;
                                                }
                                                else if (comparisonScore > 0.9)
                                                {
                                                    Console.WriteLine("Images are between 0.75 and 0.85!");
                                                    folder = between_75_and_85;
                                                }
                                                else if (comparisonScore > 0.8)
                                                {
                                                    Console.WriteLine("Images are between 0.65 and 0.75!");
                                                    folder = between_65_and_75;
                                                }
                                                else
                                                {
                                                    Console.WriteLine("Images do not match, lower than 0.65!");
                                                    folder = notMatchingFolder;
                                                }

                                                //create folder for image pair
                                                string imagePairFolder = folder + "\\" + objId.ToString();
                                                Directory.CreateDirectory(imagePairFolder);

                                                //move pair to folder
                                                File.Move(fileToCheck.FullName, imagePairFolder + "\\" + fileToCheck.Name);
                                                File.Move(downloadFilePath, imagePairFolder + "\\" + download.DownloadFileName);
                                            }
                                            else
                                            {
                                                //no asset found with this obj-id
                                                Console.WriteLine("No asset found for ObjId: " + objId.ToString());
                                                File.Move(fileToCheck.FullName, missingImgPath + "\\" + fileToCheck.Name);
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine("Filename is not an obj-id, skipping");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("No jpg, skipping..");
                                    }
                                }
                            }
                        }
                        #endregion
                        break;

                    //generate config file
                    case "2":
                        #region generate config file
                        if (File.Exists("Configuration.txt"))
                        {
                            Console.WriteLine("Configuration file already exists, overwrite with new one? (y)");
                            if (Console.ReadKey().Key.ToString().ToLower() != "y")
                            {
                                Console.WriteLine("Cancel..");
                                continue;
                            }
                        }

                        Configuration config = new Configuration()
                        {
                            ImagePath = "PATH_TO_IMAGE_FOLDER",
                            ResultPath = "PATH_TO_RESULTS_FOLDER",
                            PP_CustomerId = 0,
                            PP_ClientGUID = "PP_CLIENT_GUID",
                            PP_Email = "PP_EMAIL",
                            PP_Password = "PP_PASSWORD"
                        };

                        File.WriteAllText("Configuration.txt", JsonConvert.SerializeObject(config, Formatting.Indented));

                        FileInfo fi = new FileInfo("Configuration.txt");
                        Console.WriteLine("Configuration file generated: " + fi.FullName);
                        #endregion
                        break;

                    default:
                        Console.WriteLine("Invalid input..");
                        break;
                }
            }
        }

        private static double CompareImages(string img1Path, string img2Path)
        {
            double score = 0;

            using (var bitmap = (Bitmap)Image.FromFile(img1Path))
            {
                using (var bitmap1 = (Bitmap)Image.FromFile(img2Path))
                {
                    var hash = ImagePhash.ComputeDigest(bitmap.ToLuminanceImage());
                    var hash1 = ImagePhash.ComputeDigest(bitmap1.ToLuminanceImage());

                    score = ImagePhash.GetCrossCorrelation(hash, hash1);
                }
            }

            return score;
        }

        private static bool CheckConfiguration()
        {
            if (!File.Exists("Configuration.txt"))
            {
                Console.WriteLine("Config missing..");
                return false;
            }

            string strConfigurationFile = File.ReadAllText("Configuration.txt");

            try
            {
                Configuration = JsonConvert.DeserializeObject<Configuration>(strConfigurationFile.Replace("\\", "\\\\"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Configuration invalid, unable to parse..");
                return false;
            }

            if (!Directory.Exists(Configuration.ImagePath))
            {
                Console.WriteLine("ImagePath does not exist, please check the config file..");
                return false;
            }

            if (!Directory.Exists(Configuration.ResultPath))
            {
                Console.WriteLine("ResultPath does not exist, please check the config file..");
                return false;
            }

            if (LoginPP(PictureparkService) == null)
            {
                return false;
            }

            return true;
        }


        //pp functions
        private static CoreInfo LoginPP(ExtendedPublicServiceClient pictureParkService)
        {
            try
            {
                int customerId = Configuration.PP_CustomerId;
                int contentLanguageId = 2;
                ApplicationLanguage applLang = ApplicationLanguage.English;
                string clientGuid = Configuration.PP_ClientGUID;
                coreInfo = pictureParkService.CreateSession(
                customerId,
                applLang,
                contentLanguageId,
                null,
                null,
                clientGuid,
                null,
                null);
                coreInfo.User = new User()
                {
                    Email = Configuration.PP_Email,
                    Password = Configuration.PP_Password,
                    Language = ApplicationLanguage.English
                };
                coreInfo = pictureParkService.Login(coreInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Picturepark login failed, parameter or login wrong: " + ex.ToString());
                return null;
            }

            return coreInfo;
        }

        public static int GetFieldIdByName(List<Field> assetFields, string name)
        {
            return assetFields.SingleOrDefault(f => f.Name == name).FieldId;
        }
    }
}
