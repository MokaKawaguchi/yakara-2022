using System;
using System.Runtime.InteropServices;
using UnityEngine.XR.ARFoundation;

namespace PretiaArCloud
{
    public sealed partial class SharedAnchorSubsystem
    {
        internal enum StatusCode : int
        {
            SUCCESS = 0,

            SLAM_ERROR_API_NOT_ENABLED = 1,

            OPENVSLAM_ERROR_CONFIGURATION = 10,
            OPENVSLAM_ERROR_NOT_INITIALIZED = 11,
            OPENVSLAM_ERROR_LOAD_MAP = 12,
            OPENVSLAM_ERROR_SAVE_MAP = 13,
            OPENVSLAM_ERROR_TRACKING = 14,
            OPENVSLAM_ERROR_CREATING_ANCHOR = 15,

            IMAGE_PROC_ERROR_CONFIGURATION = 100,
            IMAGE_PROC_ERROR_NOT_INITIALIZED = 101,
            IMAGE_PROC_ERROR_FEAT_EXTRACTION = 102,

        }

        internal interface INativeApi
        {
            StatusCode InitializeSlam(string vocabPath, string deviceName, float fx, float fy, float cx, float cy, int width, int height);
            StatusCode LoadMap(string mapPath);
            StatusCode StartRelocalization();
            StatusCode RelocalizeFrame(IntPtr img, long timestampNano, ref int outFrameStatus, ref float[] outFramePose);
            StatusCode GetRelocalizationScore(ref float outScore);
            StatusCode GetRelocalizationKeypoints(out float[] outKeypoints);
            StatusCode ResetSlam();

            StatusCode InitializeORBFeatureExtraction(string deviceName, float fx, float fy, float cx, float cy, int width, int height);

            StatusCode GetYamlConfiguration(string appKey, out byte[] outConfig);

            StatusCode ExtractAndPackORBFeatures(IntPtr img, out byte[] outFeatures);
        }

        internal class NativeApi : INativeApi
        {
            private IntPtr ArCloudApi;
            private IntPtr SlamApi;
            private IntPtr ImageProcApi;

            public NativeApi()
            {
                ArCloudApi = NativePlugins.pretiaSdkCreateNativeApplication();
                SlamApi = NativePlugins.pretiaSdkGetSlamApi(ArCloudApi);
                ImageProcApi = NativePlugins.pretiaSdkGetImageProcessingApi(ArCloudApi);
            }

            ~NativeApi()
            {
                NativePlugins.pretiaSdkDestroyNativeApplication(ArCloudApi);
            }

            public StatusCode ExtractAndPackORBFeatures(IntPtr img, out byte[] outFeatures)
            {
                IntPtr featuresNative;
                ulong featuresSize = 0;
                var err = NativePlugins.pretiaSdkExtractAndPackORBFeatures(ImageProcApi, img, out featuresNative, ref featuresSize);
                outFeatures = new byte[featuresSize];
                Marshal.Copy(featuresNative, outFeatures, 0, (int)featuresSize);
                NativePlugins.pretiaSdkFreeUCharBuffer(featuresNative);
                return (StatusCode)err;
            }

            public StatusCode GetRelocalizationKeypoints(out float[] outKeypoints)
            {
                IntPtr keypointsNative;
                ulong keypointsSize = 0;
                var status = NativePlugins.pretiaSdkGetFrameTrackedKeypoints(SlamApi, ref keypointsSize, out keypointsNative);
                outKeypoints = new float[keypointsSize];
                Marshal.Copy(keypointsNative, outKeypoints, 0, (int)keypointsSize);
                NativePlugins.pretiaSdkFreeFloatBuffer(keypointsNative);
                return (StatusCode)status;
            }

            public StatusCode GetRelocalizationScore(ref float outScore)
            {
                return (StatusCode)NativePlugins.pretiaSdkGetRelocalizationScore(SlamApi, ref outScore);
            }

            public StatusCode GetYamlConfiguration(string appKey, out byte[] outConfig)
            {
                IntPtr configNative;
                ulong configSize = 0;
                var err = NativePlugins.pretiaSdkGetYamlConfiguration(ImageProcApi, appKey, out configNative, ref configSize);
                outConfig = new byte[configSize];
                Marshal.Copy(configNative, outConfig, 0, (int)configSize);
                NativePlugins.pretiaSdkFreeUCharBuffer(configNative);
                return (StatusCode)err;
            }

            public StatusCode InitializeORBFeatureExtraction(string deviceName, float fx, float fy, float cx, float cy, int width, int height)
            {
                return (StatusCode)NativePlugins.pretiaSdkInitializeORBFeatureExtraction(ImageProcApi, deviceName, width, height, cx, cy, fx, fy);
            }

            public StatusCode InitializeSlam(string vocabPath, string deviceName, float fx, float fy, float cx, float cy, int width, int height)
            {
                return (StatusCode)NativePlugins.pretiaSdkInitializeSlam(SlamApi, deviceName, vocabPath, width, height, cx, cy, fx, fy);
            }

            public StatusCode LoadMap(string mapPath)
            {
                return (StatusCode)NativePlugins.pretiaSdkLoadMapFromFile(SlamApi, mapPath);
            }

            public StatusCode RelocalizeFrame(IntPtr img, long timestampNano, ref int outFrameStatus, ref float[] outFramePose)
            {
                GCHandle handle = GCHandle.Alloc(outFramePose, GCHandleType.Pinned);
                var err = NativePlugins.pretiaSdkTrackFrame(SlamApi, img, timestampNano, ref outFrameStatus, handle.AddrOfPinnedObject(), IntPtr.Zero);
                handle.Free();
                return (StatusCode)err;
            }

            public StatusCode ResetSlam()
            {
                return (StatusCode)NativePlugins.pretiaSdkResetSlam(SlamApi);
            }

            public StatusCode StartRelocalization()
            {
                return (StatusCode)NativePlugins.pretiaSdkStartRelocalization(SlamApi);
            }
        }
    }
}