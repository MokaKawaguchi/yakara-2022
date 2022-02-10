using System;
using UnityEngine;
using System.Collections;
// #if UNITY_ANDROID
using UnityEngine.Android;
using System.Threading.Tasks;
using System.Threading;
// #endif

namespace PretiaArCloud
{
    public static class LocationProvider
    {
        public static bool RequestingLocationService { get; set; }

        /* LocationServiceStatus
            Stopped         Location service is stopped.
            Initializing    Location service is initializing, some time later it will switch to.
            Running         Location service is running and locations could be queried.
            Failed          Location service failed (user denied access to location service).
        */
        public static LocationServiceStatus Status => UnityEngine.Input.location.status;

        /* LocationInfo:
            Units are in meters. Reference: https://docs.unity3d.com/ScriptReference/LocationInfo.html

            latitude    Geographical device location latitude.
            longitude   Geographical device location latitude.
            altitude    Geographical device location altitude.
            horizontalAccuracy  Horizontal accuracy of the location.
            verticalAccuracy    Vertical accuracy of the location.
            timestamp   Timestamp (in seconds since 1970) when location was last time updated. (double)
        */
        public static double Latitude { get; private set; }

        public static double Longitude { get; private set; }

        public static double Altitude { get; private set; }

        public static float HorizontalAccuracy { get; private set; }

        public static float VerticalAccuracy { get; private set; }

        public static double Timestamp { get; private set; }

        public static bool IsLocationOn { get; private set; }

        public static string LocalTime { get; private set; }


        public static bool UserPermissionCheck
        {
            get
            {
                if (Application.platform == RuntimePlatform.Android)
                {
                    if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                    {
                        Permission.RequestUserPermission(Permission.FineLocation);
                    }

                    if (!UnityEngine.Input.location.isEnabledByUser)
                    {
                        Debug.LogFormat("LocationProvider: Android Location not enabled");
                        return false;
                    }
                }
                else if (Application.platform == RuntimePlatform.IPhonePlayer)
                {
                    if (!UnityEngine.Input.location.isEnabledByUser)
                    {
                        Debug.LogFormat("LocationProvider: IOS Location not enabled");
                        return false;
                    }
                }

                return true;
            }

            private set{}
        }

        public static async Task StartLocationServiceAsync(CancellationToken cancellationToken = default)
        {
            if(!UserPermissionCheck)
            {
                return;
            }

            /* Start service before querying location
                public void Start(float desiredAccuracyInMeters, float updateDistanceInMeters);
            */
            UnityEngine.Input.location.Start(1, 5f);
            IsLocationOn = true;

            // Wait until service initializes
            int maxWait = 15;
            while (UnityEngine.Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                maxWait--;
            }

            // Editor has a bug which doesn't set the service status to Initializing. So extra wait in Editor.
#if UNITY_EDITOR
            int editorMaxWait = 15;
            while (UnityEngine.Input.location.status == LocationServiceStatus.Stopped && editorMaxWait > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                editorMaxWait--;
            }
#endif

            // Service didn't initialize in 15 seconds
            if (maxWait < 1) {
                Debug.LogFormat("LocationProvider: Timed out");
                IsLocationOn = false;
                return;
            }

            // Connection has failed
            if (UnityEngine.Input.location.status == LocationServiceStatus.Running) {
                //created successfully, to be updated
                UpdateLocationInfo();
            }
            else
            {
                Debug.LogFormat("LocationProvider: Unable to determine device location. Failed with status {0}", UnityEngine.Input.location.status);
                IsLocationOn = false;
            }

        }

        public static IEnumerator StartLocationService()
        {
            if(!UserPermissionCheck)
            {
                yield break;
            }

            /* Start service before querying location
                public void Start(float desiredAccuracyInMeters, float updateDistanceInMeters);
            */
            UnityEngine.Input.location.Start(1, 5f);
            IsLocationOn = true;

            // Wait until service initializes
            int maxWait = 15;
            while (UnityEngine.Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                yield return new WaitForSecondsRealtime(1);
                maxWait--;
            }

            // Editor has a bug which doesn't set the service status to Initializing. So extra wait in Editor.
#if UNITY_EDITOR
            int editorMaxWait = 15;
            while (UnityEngine.Input.location.status == LocationServiceStatus.Stopped && editorMaxWait > 0)
            {
                yield return new WaitForSecondsRealtime(1);
                editorMaxWait--;
            }
#endif

            // Service didn't initialize in 15 seconds
            if (maxWait < 1) {
                Debug.LogFormat("LocationProvider: Timed out");
                IsLocationOn = false;
                yield break;
            }

            // Connection has failed
            if (UnityEngine.Input.location.status != LocationServiceStatus.Running) {
                Debug.LogFormat("LocationProvider: Unable to determine device location. Failed with status {0}", UnityEngine.Input.location.status);
                IsLocationOn = false;
                yield break;
            }
            else
            {
                //created successfully, to be updated
                UpdateLocationInfo();
            }
        } /* Create() */

        public static void StopLocationUpdates()
        {
            // Stop service if there is no need to query location updates continuously
            if(IsLocationOn)
            {
                UpdateLocationInfo();
                UnityEngine.Input.location.Stop();
                IsLocationOn = false;
            }
        }

        /*
            Format: YYYY-mm-ddTHH:MM:SS.ffffQ (e.g. 2020-10-15T11:07+09:00)
            Note: SS.ffff is optional
        */
        private static string GetLocalTime()
        {
            const string timeFmt = @"yyyy-MM-dd'T'HH:mm:ss.ffffzzz";
            DateTime currentDate = DateTime.Now;
            return currentDate.ToString(timeFmt);
        }

        public static void UpdateLocationInfo()
        {
            if (Status == LocationServiceStatus.Running)
            {
                Latitude = UnityEngine.Input.location.lastData.latitude;
                Longitude = UnityEngine.Input.location.lastData.longitude;
                Altitude =  UnityEngine.Input.location.lastData.altitude;
                HorizontalAccuracy = UnityEngine.Input.location.lastData.horizontalAccuracy;
                VerticalAccuracy = UnityEngine.Input.location.lastData.verticalAccuracy;
                Timestamp = UnityEngine.Input.location.lastData.timestamp;
                LocalTime = GetLocalTime();
            }
        }
    }
}