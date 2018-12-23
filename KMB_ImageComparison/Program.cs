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
using Emgu.CV;
using Emgu.CV.Features2D;
using Emgu.CV.Util;
using Emgu.CV.Structure;
using Emgu.CV.XFeatures2D;
using Emgu.CV.CvEnum;

namespace KMB_ImageComparison
{
    class Program
    {
        public static Field[] AssetFields;
        public static ExtendedPublicServiceClient PictureparkService = new ExtendedPublicServiceClient("Default") { };
        public static CoreInfo coreInfo;

        private static Configuration Configuration;

        private static string LogFile = "";

        static void Main(string[] args)
        {
            SiftComparison(@"C:\temp\31818.jpg", @"C:\temp\compare.jpg");

            try
            {
                if (ConfigurationManager.AppSettings["address"] != "")
                {
                    var proxy = new WebProxy(ConfigurationManager.AppSettings["address"], true);
                    proxy.Credentials = new NetworkCredential(ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["password"]);
                    WebRequest.DefaultWebProxy = proxy;
                }
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

                        LogFile = DateTime.Now.ToString("yyyyMMdd_hhmmss") + "_ImageComparison_Log.txt";
                        if (CheckConfiguration())
                        {
                            Log("Started comparison");

                            Log("Config file ok..");

                            //get asset fields
                            AssetFields = PictureparkService.GetAssetFields(coreInfo);

                            Log("Creating folder structure if not already existing");

                            //create temp folder if not already exists
                            string tempPath = Configuration.ResultPath + "\\temp";
                            if (!Directory.Exists(tempPath))
                                Directory.CreateDirectory(tempPath);

                            //higher PHASH score than 65 or SIFT matching above 100
                            string matchingFolder = Configuration.ResultPath + "\\01_matching";
                            if (!Directory.Exists(matchingFolder))
                                Directory.CreateDirectory(matchingFolder);
                            int matchingCount = 0;

                            //neither PHASH score above 65 or SIFT matching above 100
                            string noMatchingFolder = Configuration.ResultPath + "\\02_not_matching";
                            if (!Directory.Exists(noMatchingFolder))
                                Directory.CreateDirectory(noMatchingFolder);
                            int notMatchingCount = 0;

                            //create missing images folder if not already exists
                            string missingImgPath = Configuration.ResultPath + "\\03_missing_on_pp";
                            if (!Directory.Exists(missingImgPath))
                                Directory.CreateDirectory(missingImgPath);
                            int missingImgCount = 0;

                            //create multiple standard images folder if not already exists
                            string multipleStandardImgPath = Configuration.ResultPath + "\\04_multiple_w11_in_pp";
                            if (!Directory.Exists(multipleStandardImgPath))
                                Directory.CreateDirectory(multipleStandardImgPath);
                            int multipleW11Count = 0;

                            //create missing images folder if not already exists
                            string invalidAssetPath = Configuration.ResultPath + "\\05_invalid_asset_on_pp";
                            if (!Directory.Exists(invalidAssetPath))
                                Directory.CreateDirectory(invalidAssetPath);
                            int invalidAssetOnPPCount = 0;

                            int siftMatchingCount = 0;

                            //loop images in ImageFolder
                            DirectoryInfo imgDir = new DirectoryInfo(Configuration.ImagePath);

                            using (var client = new WebClient())
                            {
                                foreach (FileInfo fileToCheck in imgDir.GetFiles())
                                {
                                    coreInfo = LoginPP(PictureparkService);

                                    Console.WriteLine("Checking file: " + fileToCheck.Name);
                                    Log("Checking file: " + fileToCheck.Name);

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
                                                //more than one w11 exists for this objId
                                                Console.WriteLine("More than one w11 exists for this objId");
                                                Log("More than one w11 exists for this objId");
                                                File.Move(fileToCheck.FullName, multipleStandardImgPath + "\\" + fileToCheck.Name);
                                                multipleW11Count++;
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
                                                    Log("Problem with asset on picturepark");
                                                    invalidAssetOnPPCount++;
                                                    continue;
                                                }

                                                //download asset into temp directory
                                                string downloadFilePath = tempPath + "\\" + download.DownloadFileName;
                                                client.DownloadFile(download.URL, downloadFilePath);

                                                double phashScore = CompareImages(fileToCheck.FullName, downloadFilePath);

                                                Console.WriteLine("PHASH-Score: " + phashScore);
                                                Log("PHASH-Score: " + phashScore);

                                                int siftScore = 0;

                                                // PHASH does not match, check if SIFT finds matches
                                                if (phashScore < 0.65)
                                                {
                                                    Console.WriteLine("PHASH-Score too low, checking SIFT Algorithm");
                                                    Log("PHASH-Score too low, checking SIFT Algorithm");
                                                    siftScore = SiftComparison(fileToCheck.FullName, downloadFilePath);
                                                    Console.WriteLine("SIFT-Score: " + siftScore);
                                                    Log("SIFT-Score: " + siftScore);

                                                    if (siftScore > 100)
                                                        siftMatchingCount++;
                                                }

                                                string folder;

                                                if (phashScore > 0.65 || siftScore > 100)
                                                {
                                                    Console.WriteLine("Images match!");
                                                    Log("Images match!");
                                                    folder = matchingFolder;
                                                    matchingCount++;
                                                }
                                                else
                                                {
                                                    Console.WriteLine("Images do not match, lower PHASH than 0.65 and lower SIFT match than 100!");
                                                    Log("Images do not match, lower PHASH than 0.65 and lower SIFT match than 100!");
                                                    folder = noMatchingFolder;
                                                    notMatchingCount++;
                                                }

                                                string fullObjId = "0000000".Substring(0, 7 - objId.ToString().Length) + objId.ToString();

                                                //move pair to folder
                                                File.Move(fileToCheck.FullName, folder + "\\" + fullObjId + "_mp.jpg");
                                                File.Move(downloadFilePath, folder + "\\" + fullObjId + "_pp.jpg");
                                            }
                                            else
                                            {
                                                //no asset found with this obj-id
                                                Console.WriteLine("No asset found for ObjId: " + objId.ToString());
                                                Log("No asset found for ObjId: " + objId.ToString());
                                                File.Move(fileToCheck.FullName, missingImgPath + "\\" + fileToCheck.Name);
                                                missingImgCount++;
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine("Filename is not an obj-id, skipping");
                                            Log("Filename is not an obj-id, skipping");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("No jpg, skipping..");
                                        Log("No jpg, skipping..");
                                    }
                                }

                                Console.WriteLine("-------------------------");
                                Console.WriteLine("Image comparison finished");
                                Console.WriteLine("-------------------------");
                                Console.WriteLine("Total matches: " + matchingCount);
                                Console.WriteLine("Matches with PHASH: " + ((int)matchingCount - (int)siftMatchingCount).ToString());
                                Console.WriteLine("Matches with SIFT: " + siftMatchingCount);
                                Console.WriteLine("-------------------------");
                                Console.WriteLine("Not matching: " + notMatchingCount);
                                Console.WriteLine("-------------------------");
                                Console.WriteLine("Missing assets in Picturepark: " + missingImgCount);
                                Console.WriteLine("-------------------------");
                                Console.WriteLine("Multiple W11 in Picturepark: " + multipleW11Count);
                                Console.WriteLine("-------------------------");
                                Console.WriteLine("Invalid assets in Picturepark: " + invalidAssetOnPPCount);
                                Console.WriteLine("-------------------------");

                                Log("-------------------------");
                                Log("Image comparison finished");
                                Log("-------------------------");
                                Log("Total matches: " + matchingCount);
                                Log("Matches with PHASH: " + matchingCount + siftMatchingCount);
                                Log("Matches with SIFT: " + siftMatchingCount);
                                Log("-------------------------");
                                Log("Not matching: " + notMatchingCount);
                                Log("-------------------------");
                                Log("Missing assets in Picturepark: " + missingImgCount);
                                Log("-------------------------");
                                Log("Multiple W11 in Picturepark: " + multipleW11Count);
                                Log("-------------------------");
                                Log("Invalid assets in Picturepark: " + invalidAssetOnPPCount);
                                Log("-------------------------");
                            }
                        }
                        else
                        {
                            Log("Config file invalid, aborting");
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
                            LogPath = "PATH_TO_LOG_FOLDER",
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
                Log("Config missing..");
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
                Log("Configuration invalid, unable to parse..");
                return false;
            }

            if (!Directory.Exists(Configuration.ImagePath))
            {
                Console.WriteLine("ImagePath does not exist, please check the config file..");
                Log("ImagePath does not exist, please check the config file..");
                return false;
            }

            if (!Directory.Exists(Configuration.ResultPath))
            {
                Console.WriteLine("ResultPath does not exist, please check the config file..");
                Log("ResultPath does not exist, please check the config file..");
                return false;
            }

            if (LoginPP(PictureparkService) == null)
            {
                return false;
            }

            return true;
        }

        private static void Log(string logLine)
        {
            try
            {
                logLine = DateTime.Now.ToString("HH:mm") + ": " + logLine;
                File.AppendAllLines(Configuration.LogPath + "\\" + LogFile, new List<string>() { logLine });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable to log: " + ex.ToString());
                Console.ReadLine();
            }
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

        public static int SiftComparison(string img1, string img2)
        {
            var sift = new Emgu.CV.XFeatures2D.SIFT();

            var modelKeyPoints = new VectorOfKeyPoint();
            Mat modelDescriptors = new Mat();

            var observedKeyPoints = new VectorOfKeyPoint();
            Mat observedDescriptors = new Mat();
            Mat mask = new Mat();

            VectorOfVectorOfDMatch matches = new VectorOfVectorOfDMatch();
            int k = 2;
            double uniquenessThreshold = 0.80;

            using (Mat modelImage = CvInvoke.Imread(img1, ImreadModes.Grayscale))
            using (Mat observedImage = CvInvoke.Imread(img2, ImreadModes.Grayscale))
            {
                sift.DetectAndCompute(modelImage, null, modelKeyPoints, modelDescriptors, false);
                sift.DetectAndCompute(observedImage, null, observedKeyPoints, observedDescriptors, false);
                BFMatcher matcher = new BFMatcher(DistanceType.L1);

                matcher.Add(modelDescriptors);
                //matcher.Add(observedDescriptors);

                matcher.KnnMatch(observedDescriptors, matches, k, null);
                mask = new Mat(matches.Size, 1, DepthType.Cv8U, 1);
                mask.SetTo(new MCvScalar(255));
                Features2DToolbox.VoteForUniqueness(matches, uniquenessThreshold, mask);
            }

            int score = 0;
            for (int i = 0; i < matches.Size; i++)
            {
                if (mask.GetData(i)[0] == 0) continue;
                foreach (var e in matches[i].ToArray())
                    ++score;
            }

            return score;
        }
    }
}
