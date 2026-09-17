using System;
using System.Collections.Generic;
using System.Net;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

public class AssetInfo {
  public const string ASSET_NAME = "SCI FI CREATURES PACK VOL 2";

  public const string INSTALLED_VERSION = "1.11";

  public const string MIN_UNITY_VERSION = "2021.3.16f1";
  public const string MIN_URP_VERSION = "12.1.6";


#if !URP //Enabled when com.unity.render-pipelines.universal is below MIN_URP_VERSION
  [InitializeOnLoad]
  sealed class PackageInstaller : Editor {
    [InitializeOnLoadMethod]
    public static void Initialize() {
      GetLatestCompatibleURPVersion();

      if (EditorUtility.DisplayDialog(ASSET_NAME + " v" + INSTALLED_VERSION, "This package requires the Universal Render Pipeline " + MIN_URP_VERSION + " or newer, would you like to install or update it now?", "OK", "Later")) {
        Debug.Log("Universal Render Pipeline <b>v" + lastestURPVersion + "</b> will start installing in a moment. Please refer to the URP documentation for set up instructions");
        Debug.Log("After installing and setting up URP, you must Re-import the Shaders folder!");

        InstallURP();
      }
    }

    private static PackageInfo urpPackage;

    private static string lastestURPVersion;

#if SWS_DEV
            [MenuItem("SWS/Get latest URP version")]
#endif
    private static void GetLatestCompatibleURPVersion() {
      if (urpPackage == null) urpPackage = GetURPPackage();
      if (urpPackage == null) return;

      lastestURPVersion = urpPackage.versions.latestCompatible;

#if SWS_DEV
                Debug.Log("Latest compatible URP version: " + lastestURPVersion);
#endif
    }

    private static void InstallURP() {
      if (urpPackage == null) urpPackage = GetURPPackage();
      if (urpPackage == null) return;

      lastestURPVersion = urpPackage.versions.latestCompatible;

      AddRequest addRequest = Client.Add(URP_PACKAGE_ID + "@" + lastestURPVersion);

      //Update Core and Shader Graph packages as well, doesn't always happen automatically
      for (int i = 0; i < urpPackage.dependencies.Length; i++) {
#if SWS_DEV
                    Debug.Log("Updating URP dependency <i>" + urpPackage.dependencies[i].name + "</i> to " + urpPackage.dependencies[i].version);
#endif
        addRequest = Client.Add(urpPackage.dependencies[i].name + "@" + urpPackage.dependencies[i].version);
      }

      //Wait until finished
      while (!addRequest.IsCompleted || addRequest.Status == StatusCode.InProgress) { }
    }
  }
#endif

  public const string URP_PACKAGE_ID = "com.unity.render-pipelines.universal";

  public static PackageInfo GetURPPackage() {
    SearchRequest request = Client.Search(URP_PACKAGE_ID);

    while (request.Status == StatusCode.InProgress) {
      /* Waiting... */
    }

    if (request.Status == StatusCode.Failure) {
      Debug.LogError("Failed to retrieve URP package from Package Manager...");
      return null;
    }

    return request.Result[0];
  }
}