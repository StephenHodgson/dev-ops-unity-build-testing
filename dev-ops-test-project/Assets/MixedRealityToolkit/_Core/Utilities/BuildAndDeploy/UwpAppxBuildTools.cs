﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Core.Extensions;
using Microsoft.MixedReality.Toolkit.Core.Utilities.Editor;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Microsoft.MixedReality.Toolkit.Core.Utilities.Build
{
    public class UwpAppxBuildTools
    {
        private static readonly XNamespace UapNameSpace = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
        private static readonly XNamespace Uap5NameSpace = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";

        /// <summary>
        /// Query the build process to see if we're already building.
        /// </summary>
        public static bool IsBuilding { get; private set; } = false;

        /// <summary>
        /// Build the UWP appx bundle for this project.  Requires that <see cref="UwpPlayerBuildTools.BuildPlayer(string,bool,bool)"/> has already be run or a user has
        /// previously built the Unity Player with the WSA Player as the Build Target.
        /// </summary>
        /// <param name="buildInfo"></param>
        /// <returns>True, if the appx build was successful.</returns>
        public static async Task<bool> BuildAppxAsync(UwpBuildInfo buildInfo)
        {
            if (!EditorAssemblyReloadManager.LockReloadAssemblies)
            {
                Debug.LogError("Lock Reload assemblies before attempting to build appx!");
                return false;
            }

            if (IsBuilding)
            {
                Debug.LogWarning("Build already in progress!");
                return false;
            }

            if (Application.isBatchMode)
            {
                // We don't need stack traces on all our logs. Makes things a lot easier to read.
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            }

            Debug.Log("Starting Unity Appx Build...");

            IsBuilding = true;
            string slnFilename = Path.Combine(buildInfo.OutputDirectory, $"{PlayerSettings.productName}.sln");

            if (!File.Exists(slnFilename))
            {
                Debug.LogError("Unable to find Solution to build from!");
                return IsBuilding = false;
            }

            // Get and validate the msBuild path...
            var msBuildPath = await FindMsBuildPathAsync();

            if (!File.Exists(msBuildPath))
            {
                Debug.LogError($"MSBuild.exe is missing or invalid!\n{msBuildPath}");
                return IsBuilding = false;
            }

            // Ensure that the generated .appx version increments by modifying Package.appxmanifest
            if (!UpdateAppxManifest(buildInfo))
            {
                Debug.LogError("Failed to increment package version!");
                return IsBuilding = false;
            }

            string storagePath = Path.GetFullPath(Path.Combine(Path.Combine(Application.dataPath, ".."), buildInfo.OutputDirectory));
            string solutionProjectPath = Path.GetFullPath(Path.Combine(storagePath, $@"{PlayerSettings.productName}.sln"));

            // Now do the actual appx build
            var processResult = await new Process().StartProcessAsync(
                msBuildPath,
                $"\"{solutionProjectPath}\" /t:{(buildInfo.RebuildAppx ? "Rebuild" : "Build")} /p:Configuration={buildInfo.Configuration} /p:Platform={buildInfo.BuildPlatform} /verbosity:m",
                !Application.isBatchMode);

            switch (processResult.ExitCode)
            {
                case 0:
                    Debug.Log("Appx Build Successful!");

                    if (Application.isBatchMode)
                    {
                        Debug.Log(string.Join("\n", processResult.Output));
                    }
                    break;
                case -1073741510:
                    Debug.LogWarning("The build was terminated either by user's keyboard input CTRL+C or CTRL+Break or closing command prompt window.");
                    break;
                default:
                    {
                        if (processResult.ExitCode != 0)
                        {
                            Debug.LogError($"{PlayerSettings.productName} appx build Failed! (ErrorCode: {processResult.ExitCode})");

                            if (Application.isBatchMode)
                            {
                                Debug.LogError(string.Join("\n", processResult.Errors));
                            }
                        }

                        break;
                    }
            }

            AssetDatabase.SaveAssets();

            IsBuilding = false;
            return processResult.ExitCode == 0;
        }

        private static async Task<string> FindMsBuildPathAsync()
        {
            var result = await new Process().StartProcessAsync(
                new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Arguments = $@"/C vswhere -all -products * -requires Microsoft.Component.MSBuild -property installationPath",
                    WorkingDirectory = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer"
                });

            foreach (var path in result.Output)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    string[] paths = path.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                    if (paths.Length > 0)
                    {
                        // if there are multiple visual studio installs,
                        // prefer enterprise, then pro, then community
                        string bestPath = paths.OrderBy(p => p.ToLower().Contains("enterprise"))
                            .ThenBy(p => p.ToLower().Contains("professional"))
                            .ThenBy(p => p.ToLower().Contains("community")).First();

                        return $@"{bestPath}\MSBuild\15.0\Bin\MSBuild.exe";
                    }
                }
            }

            return string.Empty;
        }

        private static bool UpdateAppxManifest(UwpBuildInfo buildInfo)
        {
            // Find the manifest, assume the one we want is the first one
            string[] manifests = Directory.GetFiles(BuildDeployPreferences.AbsoluteBuildDirectory, "Package.appxmanifest", SearchOption.AllDirectories);

            if (manifests.Length == 0)
            {
                Debug.LogError($"Unable to find Package.appxmanifest file for build (in path - {BuildDeployPreferences.AbsoluteBuildDirectory})");
                return false;
            }

            if (manifests.Length > 1)
            {
                Debug.LogWarning("Found more than one appxmanifest in the target build folder!");
            }

            const string uap5 = "uap5";

            var rootNode = XElement.Load(manifests[0]);
            var identityNode = rootNode.Element(rootNode.GetDefaultNamespace() + "Identity");

            if (identityNode == null)
            {
                Debug.LogError($"Package.appxmanifest for build (in path - {BuildDeployPreferences.AbsoluteBuildDirectory}) is missing an <Identity /> node");
                return false;
            }

            var dependencies = rootNode.Element(rootNode.GetDefaultNamespace() + "Dependencies");

            if (dependencies == null)
            {
                Debug.LogError($"Package.appxmanifest for build (in path - {BuildDeployPreferences.AbsoluteBuildDirectory}) is missing <Dependencies /> node.");
                return false;
            }

            UpdateDependenciesElement(dependencies, rootNode.GetDefaultNamespace());

            // Setup the 3d app icon.
            if (!string.IsNullOrWhiteSpace(UwpBuildDeployPreferences._3DAppIconPath))
            {
                // Add the uap5 namespace if it doesn't exist.
                if (rootNode.GetNamespaceOfPrefix(uap5) == null)
                {
                    rootNode.Add(new XAttribute(XNamespace.Xmlns + uap5, Uap5NameSpace));
                }

                var ignorable = rootNode.Attribute(XName.Get("IgnorableNamespaces"));

                if (ignorable != null)
                {
                    if (!ignorable.Value.Contains(uap5))
                    {
                        ignorable.Value = $"{ignorable.Value} {uap5}";
                    }
                }

                // find mixed reality model
                var modelContainer = rootNode.Descendants(Uap5NameSpace + "MixedRealityModel").ToArray();

                if (modelContainer.Any())
                {
                    var element = modelContainer.First();
                    var path = element.Attribute(XName.Get("Path"));

                    if (path != null)
                    {
                        path.Value = buildInfo.AppIconPath;
                    }
                    else
                    {
                        element.Add(new XAttribute("Path", buildInfo.AppIconPath));
                    }
                }
                else
                {
                    var modelElement = new XElement(Uap5NameSpace + "MixedRealityModel");
                    var defaultTile = rootNode.Descendants(UapNameSpace + "DefaultTile").First();
                    defaultTile.Add(modelElement);
                    modelElement.Add(new XAttribute("Path", buildInfo.AppIconPath));
                }
            }

            // We use XName.Get instead of string -> XName implicit conversion because
            // when we pass in the string "Version", the program doesn't find the attribute.
            // Best guess as to why this happens is that implicit string conversion doesn't set the namespace to empty
            var versionAttr = identityNode.Attribute(XName.Get("Version"));

            if (versionAttr == null)
            {
                Debug.LogError($"Package.appxmanifest for build (in path - {BuildDeployPreferences.AbsoluteBuildDirectory}) is missing a Version attribute in the <Identity /> node.");
                return false;
            }

            // Assume package version always has a '.' between each number.
            // According to https://msdn.microsoft.com/en-us/library/windows/apps/br211441.aspx
            // Package versions are always of the form Major.Minor.Build.Revision.
            // Note: Revision number reserved for Windows Store, and a value other than 0 will fail WACK.
            var version = PlayerSettings.WSA.packageVersion;
            var newVersion = new Version(version.Major, version.Minor, buildInfo.AutoIncrement ? version.Build + 1 : version.Build, version.Revision);

            PlayerSettings.WSA.packageVersion = newVersion;
            versionAttr.Value = newVersion.ToString();
            rootNode.Save(manifests[0]);
            return true;
        }

        private static void UpdateDependenciesElement(XElement dependencies, XNamespace defaultNamespace)
        {
            var values = (PlayerSettings.WSATargetFamily[])Enum.GetValues(typeof(PlayerSettings.WSATargetFamily));

            if (string.IsNullOrWhiteSpace(EditorUserBuildSettings.wsaUWPSDK))
            {
                var windowsSdkPaths = Directory.GetDirectories(@"C:\Program Files (x86)\Windows Kits\10\Lib");

                for (int i = 0; i < windowsSdkPaths.Length; i++)
                {
                    windowsSdkPaths[i] = windowsSdkPaths[i].Substring(windowsSdkPaths[i].LastIndexOf(@"\", StringComparison.Ordinal) + 1);
                }

                EditorUserBuildSettings.wsaUWPSDK = windowsSdkPaths[windowsSdkPaths.Length - 1];
            }

            string maxVersionTested = EditorUserBuildSettings.wsaUWPSDK;

            if (string.IsNullOrWhiteSpace(EditorUserBuildSettings.wsaMinUWPSDK))
            {
                EditorUserBuildSettings.wsaMinUWPSDK = UwpBuildDeployPreferences.MIN_SDK_VERSION;
            }

            string minVersion = EditorUserBuildSettings.wsaMinUWPSDK;

            // Clear any we had before.
            dependencies.RemoveAll();

            foreach (PlayerSettings.WSATargetFamily family in values)
            {
                if (PlayerSettings.WSA.GetTargetDeviceFamily(family))
                {
                    dependencies.Add(
                        new XElement(defaultNamespace + "TargetDeviceFamily",
                        new XAttribute("Name", $"Windows.{family}"),
                        new XAttribute("MinVersion", minVersion),
                        new XAttribute("MaxVersionTested", maxVersionTested)));
                }
            }

            if (!dependencies.HasElements)
            {
                dependencies.Add(
                    new XElement(defaultNamespace + "TargetDeviceFamily",
                    new XAttribute("Name", "Windows.Universal"),
                    new XAttribute("MinVersion", minVersion),
                    new XAttribute("MaxVersionTested", maxVersionTested)));
            }
        }
    }
}