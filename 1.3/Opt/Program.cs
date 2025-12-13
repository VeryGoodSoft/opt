using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO.Compression;

namespace Opt
{
    public class PackageInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public static class Program
    {
        const string ApiUrl = "https://optdata.vercel.app/packages.json";
        const string InstalledFile = "installed_packages.json";
        const string InstallFolder = "packages";

        public static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return;
            }

            Directory.CreateDirectory(InstallFolder);

            switch (args[0].ToLower())
            {
                case "install":
                    if (args.Length < 2) { Console.WriteLine("Specify package name."); return; }
                    await InstallPackage(args[1]);
                    break;
                case "uninstall":
                    if (args.Length < 2) { Console.WriteLine("Specify package name."); return; }
                    UninstallPackage(args[1]);
                    break;
                case "list":
                    ListInstalledPackages();
                    break;
                case "version":
                    if (args.Length < 2) { Console.WriteLine("Specify package name."); return; }
                    ShowVersion(args[1]);
                    break;
                case "info":
                    if (args.Length < 2) { Console.WriteLine("Specify package name."); return; }
                    await ShowInfo(args[1]);
                    break;
                case "update":
                    if (args.Length < 2)
                        await UpdateAllPackages();
                    else
                        await UpdatePackage(args[1]);
                    break;
                case "search":
                    if (args.Length < 2) { Console.WriteLine("Specify search query."); return; }
                    await SearchPackages(args[1]);
                    break;
                default:
                    Console.WriteLine("Unknown command.");
                    PrintHelp();
                    break;
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("OPT Package Manager Commands:");
            Console.WriteLine("  opt install <package>");
            Console.WriteLine("  opt uninstall <package>");
            Console.WriteLine("  opt list");
            Console.WriteLine("  opt version <package>");
            Console.WriteLine("  opt info <package>");
            Console.WriteLine("  opt update [package]  # update specific or all installed packages");
            Console.WriteLine("  opt search <query>");
        }

        static async Task<Dictionary<string, PackageInfo>> LoadRemotePackages()
        {
            using var client = new HttpClient();
            try
            {
                var json = await client.GetStringAsync(ApiUrl);
                return JsonConvert.DeserializeObject<Dictionary<string, PackageInfo>>(json)
                       ?? new Dictionary<string, PackageInfo>();
            }
            catch
            {
                Console.WriteLine("Failed to fetch package list from remote API.");
                return new Dictionary<string, PackageInfo>();
            }
        }

        static Dictionary<string, string> LoadInstalled()
        {
            if (!File.Exists(InstalledFile)) return new Dictionary<string, string>();
            var text = File.ReadAllText(InstalledFile);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(text)
                   ?? new Dictionary<string, string>();
        }

        static void SaveInstalled(Dictionary<string, string> dict)
        {
            File.WriteAllText(InstalledFile, JsonConvert.SerializeObject(dict, Formatting.Indented));
        }

        static async Task DownloadFileWithProgress(string url, string outputPath)
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = total != -1;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                totalRead += read;
                if (canReportProgress)
                {
                    Console.Write($"\rDownloading: {totalRead * 100 / total}%");
                }
            }
            if (canReportProgress) Console.WriteLine("\rDownload complete!      ");
        }

        static async Task InstallPackage(string name)
        {
            var packages = await LoadRemotePackages();
            if (!packages.TryGetValue(name, out var pkg))
            {
                Console.WriteLine($"Package '{name}' not found in API.");
                return;
            }

            var localPath = Path.Combine(InstallFolder, name + ".zip");

            try
            {
                Console.WriteLine($"Downloading {name} version {pkg.Version}...");
                await DownloadFileWithProgress(pkg.Url, localPath);

                var extractPath = Path.Combine(InstallFolder, name);
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                ZipFile.ExtractToDirectory(localPath, extractPath);

                var installed = LoadInstalled();
                installed[name] = pkg.Version;
                SaveInstalled(installed);

                Console.WriteLine($"Package '{name}' installed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to install '{name}': {ex.Message}");
            }
        }

        static void UninstallPackage(string name)
        {
            var installed = LoadInstalled();
            if (!installed.Remove(name))
            {
                Console.WriteLine($"Package '{name}' is not installed.");
                return;
            }

            var packagePath = Path.Combine(InstallFolder, name);
            if (Directory.Exists(packagePath))
                Directory.Delete(packagePath, true);

            SaveInstalled(installed);
            Console.WriteLine($"Package '{name}' uninstalled successfully.");
        }

        static void ShowVersion(string name)
        {
            var installed = LoadInstalled();
            if (installed.TryGetValue(name, out var version))
                Console.WriteLine($"{name}: {version}");
            else
                Console.WriteLine($"Package '{name}' is not installed.");
        }

        static void ListInstalledPackages()
        {
            var installed = LoadInstalled();
            if (installed.Count == 0)
            {
                Console.WriteLine("No packages installed.");
                return;
            }

            Console.WriteLine("Installed packages:");
            foreach (var kv in installed)
                Console.WriteLine($" - {kv.Key} (version {kv.Value})");
        }

        static async Task ShowInfo(string name)
        {
            var packages = await LoadRemotePackages();
            var installed = LoadInstalled();

            if (!packages.TryGetValue(name, out var pkg))
            {
                Console.WriteLine($"Package '{name}' not found.");
                return;
            }

            Console.WriteLine($"Package: {name}");
            Console.WriteLine($"Description: {pkg.Description ?? "No description"}");
            Console.WriteLine($"Latest version: {pkg.Version}");
            Console.WriteLine($"Download URL: {pkg.Url}");

            if (installed.TryGetValue(name, out var installedVersion))
                Console.WriteLine($"Installed: Yes ({installedVersion})");
            else
                Console.WriteLine("Installed: No");
        }

        static async Task UpdatePackage(string name)
        {
            var packages = await LoadRemotePackages();
            if (!packages.TryGetValue(name, out var pkg))
            {
                Console.WriteLine($"Package '{name}' not found in API.");
                return;
            }

            var installed = LoadInstalled();
            if (!installed.TryGetValue(name, out var installedVersion))
            {
                Console.WriteLine($"Package '{name}' is not installed. Use 'opt install {name}' instead.");
                return;
            }

            if (installedVersion == pkg.Version)
            {
                Console.WriteLine($"Package '{name}' is already up-to-date ({installedVersion}).");
                return;
            }

            Console.WriteLine($"Updating {name} from version {installedVersion} to {pkg.Version}...");

            var localPath = Path.Combine(InstallFolder, name + ".zip");

            try
            {
                await DownloadFileWithProgress(pkg.Url, localPath);

                var extractPath = Path.Combine(InstallFolder, name);
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                ZipFile.ExtractToDirectory(localPath, extractPath);

                installed[name] = pkg.Version;
                SaveInstalled(installed);

                Console.WriteLine($"Package '{name}' updated successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update '{name}': {ex.Message}");
            }
        }

        static async Task UpdateAllPackages()
        {
            var installed = LoadInstalled();
            if (installed.Count == 0)
            {
                Console.WriteLine("No packages installed to update.");
                return;
            }

            foreach (var pkgName in installed.Keys)
            {
                await UpdatePackage(pkgName);
            }
        }

        static async Task SearchPackages(string query)
        {
            var packages = await LoadRemotePackages();
            bool found = false;
            foreach (var kv in packages)
            {
                if (kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (kv.Value.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    Console.WriteLine($" - {kv.Key} ({kv.Value.Version}) : {kv.Value.Description}");
                    found = true;
                }
            }
            if (!found) Console.WriteLine("No packages found.");
        }
    }
}
