using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PretiaArCloud
{
    internal partial class NativePlugins
    {
#if UNITY_EDITOR || UNITY_STANDALONE

        public static int pretiaSdkInitializeSlam(
            IntPtr slamApi,
            string deviceName,
            string vocabFilepath,
            int cols, int rows,
            double cx, double cy, double fx, double fy) { return 0; }
        
        public static int pretiaSdkLoadMapFromFile(IntPtr slamApi, string mapFilepath) { return 0; }

        public static int pretiaSdkStartRelocalization(IntPtr slamApi) { return 0; }

        public static int pretiaSdkTrackFrame(
            IntPtr slamApi,
            IntPtr img,
            Int64 timestampNano,
            ref int outFrameStatus,
            IntPtr outPoseCw,
            IntPtr odomPoseWc) { return 0; }

        public static int pretiaSdkGetRelocalizationScore(IntPtr slamApi, ref float outScore) { return 0; }

        public static int pretiaSdkGetFrameTrackedKeypoints(IntPtr slamApi, ref UInt64 pointsSize, out IntPtr points)
        {
            points = IntPtr.Zero;
            return 0;
        }
        
        public static int pretiaSdkResetSlam(IntPtr slamApi) { return 0; }

#else

        [DllImport(ApiConstants.PRETIA_SDK_NATIVE_LIB)]
        public static extern int pretiaSdkInitializeSlam(
            IntPtr slamApi,
            string deviceName,
            string vocabFilepath,
            int cols, int rows,
            double cx, double cy, double fx, double fy);

        [DllImport(ApiConstants.PRETIA_SDK_NATIVE_LIB)]
        public static extern int pretiaSdkLoadMapFromFile(IntPtr slamApi, string mapFilepath);

        [DllImport(ApiConstants.PRETIA_SDK_NATIVE_LIB)]
        public static extern int pretiaSdkStartRelocalization(IntPtr slamApi);

        [DllImport(ApiConstants.PRETIA_SDK_NATIVE_LIB)]
        public static extern int pretiaSdkTrackFrame(
            IntPtr slamApi,
            IntPtr img,
            Int64 timestampNano,
            ref int outFrameStatus,
            IntPtr outPoseCw,
            IntPtr odomPoseWc);

        [DllImport(ApiConstants.PRETIA_SDK_NATIVE_LIB)]
        public static extern int pretiaSdkGetRelocalizationScore(IntPtr slamApi, ref float outScore);

        [DllImport(ApiConstants.PRETIA_SDK_NATIVE_LIB)]
        public static extern int pretiaSdkGetFrameTrackedKeypoints(IntPtr slamApi, ref UInt64 pointsSize, out IntPtr points);

        [DllImport(ApiConstants.PRETIA_SDK_NATIVE_LIB)]
        public static extern int pretiaSdkResetSlam(IntPtr slamApi);

#endif

    }
}