using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PretiaArCloud
{
    internal class Utility
    {
        static Matrix4x4 Mat_rot90neg = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, -90), Vector3.one);
        static Matrix4x4 Mat_rot180 = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 180), Vector3.one);
        static Matrix4x4 Mat_rot90pos = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), Vector3.one);
        static Matrix4x4 Mat_rotNone = Matrix4x4.identity;

        internal static Pose EstimateSharedAnchor(Pose Tcw_slam, Pose Twu_unity)
        {
            // c = SLAM camera
            // u = Unity camera
            // _slam = SLAM coordinate system
            // _unity = Unity coordinate system

            // Tcw_slam = T slam camera to world, in SLAM coordinate system
            // Twu_unity = T world to unity camera, in Unity coordinate system


            var Mat_cw_slam = Matrix4x4.TRS(Tcw_slam.position, Tcw_slam.rotation, Vector3.one);
            
            // SLAM Tcw --> Twc
            Matrix4x4 Mat_wc_slam = default;
            Matrix4x4.Inverse3DAffine(Mat_cw_slam, ref Mat_wc_slam);

            var T_wc_slam = new Pose(Mat_wc_slam.GetColumn(3), Mat_wc_slam.rotation);


            // Compute SLAM pose in Unity coordinate system (RHS/VSlam --> LHS/Unity/ARKit)
            // and account for screen rotation
            Matrix4x4 Mat_wc_unity = Matrix4x4.identity;
            var T_wc_unity = T_wc_slam;
            switch (Screen.orientation)
            {
                case ScreenOrientation.LandscapeLeft:
                T_wc_unity.position.y = -T_wc_slam.position.y;
                T_wc_unity.rotation.y = -T_wc_slam.rotation.y;
                T_wc_unity.rotation.w = -T_wc_slam.rotation.w;
                Mat_wc_unity = Matrix4x4.TRS(T_wc_unity.position, T_wc_unity.rotation, Vector3.one);
                break;

                case ScreenOrientation.PortraitUpsideDown:
                T_wc_unity.position.x = T_wc_slam.position.y;
                T_wc_unity.position.y = T_wc_slam.position.x;
                T_wc_unity.rotation.x = T_wc_slam.rotation.y;
                T_wc_unity.rotation.y = T_wc_slam.rotation.x;
                T_wc_unity.rotation.w = -T_wc_slam.rotation.w;
                Mat_wc_unity = Matrix4x4.TRS(T_wc_unity.position, T_wc_unity.rotation, Vector3.one);
                Mat_wc_unity = Mat_rot90neg * Mat_wc_unity;
                break;

                case ScreenOrientation.LandscapeRight:
                T_wc_unity.position.x = -T_wc_slam.position.x;
                T_wc_unity.rotation.x = -T_wc_slam.rotation.x;
                T_wc_unity.rotation.w = -T_wc_slam.rotation.w;
                Mat_wc_unity = Matrix4x4.TRS(T_wc_unity.position, T_wc_unity.rotation, Vector3.one);
                Mat_wc_unity = Mat_rot180 * Mat_wc_unity;
                break;

                case ScreenOrientation.Portrait:
                default:
                T_wc_unity.position.x = -T_wc_slam.position.y;
                T_wc_unity.position.y = -T_wc_slam.position.x;
                T_wc_unity.rotation.x = -T_wc_slam.rotation.y;
                T_wc_unity.rotation.y = -T_wc_slam.rotation.x;
                T_wc_unity.rotation.w = -T_wc_slam.rotation.w;
                Mat_wc_unity = Matrix4x4.TRS(T_wc_unity.position, T_wc_unity.rotation, Vector3.one);
                Mat_wc_unity = Mat_rot90pos * Mat_wc_unity;
                break;
            }


            Matrix4x4 Mat_cw_unity = default;
            Matrix4x4.Inverse3DAffine(Mat_wc_unity, ref Mat_cw_unity);

            var Mat_wu_unity = Matrix4x4.TRS(Twu_unity.position, Twu_unity.rotation, Vector3.one);

            // Calculate shared anchor position
            var Mat_wu_cw_unity = Mat_wu_unity * Mat_cw_unity;

            var Twu_cw_unity = new Pose(Mat_wu_cw_unity.GetColumn(3), Mat_wu_cw_unity.rotation);
            return Twu_cw_unity;
        }

        internal static Quaternion QuaternionTwist(Quaternion q, Vector3 twistAxis)
        {
            Vector3 r = new Vector3(q.x, q.y, q.z);
            if (r.sqrMagnitude < Mathf.Epsilon)
            {
                // always twist 180 degree on singularity
                return Quaternion.AngleAxis(180.0f, twistAxis);
            }
            else
            {
                Vector3 p = Vector3.Project(r, twistAxis);
                Quaternion twist = new Quaternion(p.x, p.y, p.z, q.w);
                twist = Quaternion.Normalize(twist);
                return twist;
            }
        }

        internal static void QuaternionSwingTwist(
            Quaternion q,
            Vector3 twistAxis,
            out Quaternion swing,
            out Quaternion twist)
        {
            Vector3 r = new Vector3(q.x, q.y, q.z);
            
            // singularity: rotation by 180 degree
            if (r.sqrMagnitude < Mathf.Epsilon)
            {
                // always twist 180 degree on singularity
                twist = Quaternion.AngleAxis(180.0f, twistAxis);

                Vector3 rotatedTwistAxis = q * twistAxis;
                Vector3 swingAxis = Vector3.Cross(twistAxis, rotatedTwistAxis);
            
                if (swingAxis.sqrMagnitude > Mathf.Epsilon)
                {
                    float swingAngle = 
                        Vector3.Angle(twistAxis, rotatedTwistAxis);
                    swing = Quaternion.AngleAxis(swingAngle, swingAxis);
                }
                else
                {
                    // more singularity: 
                    // rotation axis parallel to twist axis
                    swing = Quaternion.identity; // no swing
                }
                return;
            }
            
            // meat of swing-twist decomposition
            Vector3 p = Vector3.Project(r, twistAxis);
            twist = new Quaternion(p.x, p.y, p.z, q.w);
            twist = Quaternion.Normalize(twist);

            swing = q * Quaternion.Inverse(twist);
        }

        internal static Pose SetPoseUpward(Pose pose)
        {
            Pose posePointingUp = new Pose();
            posePointingUp.position = pose.position;
            posePointingUp.rotation = QuaternionTwist(pose.rotation, new Vector3(0,1,0));
            return posePointingUp;
        }

        internal static Pose GetAveragePose(List<UnityEngine.XR.ARSubsystems.XRAnchor> anchors)
        {
            if (anchors.Count == 0)
            {
                throw new ArgumentException("Not enough anchors to average");
            }

            // Only average the position
            // Note: the previous version of the SDK used a median on each coordinate
            Vector3 averagePos = Vector3.zero;
            foreach (var anchor in anchors)
            {
                averagePos += anchor.pose.position;
            }
            
            return new Pose(averagePos / anchors.Count, anchors[anchors.Count-1].pose.rotation);
        }

        internal static bool IsEditor() => (
            Application.platform == RuntimePlatform.OSXEditor
            || Application.platform == RuntimePlatform.LinuxEditor
            || Application.platform == RuntimePlatform.WindowsEditor
        );

        internal static bool IsStandalone() => (
            Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.LinuxPlayer
            || Application.platform == RuntimePlatform.OSXPlayer
        );

        internal static byte[] StructToBytes<T>(T str) where T: struct
        {
            int size = Marshal.SizeOf(str);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr<T>(str, ptr, fDeleteOld: false);
            Marshal.Copy(ptr, arr, 0, size);
            return arr;
        }

        unsafe internal static T SpanToStruct<T>(ReadOnlySpan<byte> span) where T: struct
        {
            fixed (byte* ptr = &span[0])
            {
                return (T)Marshal.PtrToStructure((IntPtr)ptr, typeof(T));
            }
        }

        internal static Pose RawPoseToUnityPose(float[] pose)
        {
            return new Pose(
                new Vector3(pose[4], pose[5], pose[6]),
                new Quaternion(pose[0], pose[1], pose[2], pose[3]));
        }

        internal static string GetDeviceName()
        {
            return $"{SystemInfo.deviceModel} {SystemInfo.deviceUniqueIdentifier}";
        }

        internal static async Task StreamingAssetsToFile(
            string inputAssetsPath, string outputFilePath,
            CancellationToken cancellationToken = default)
        {
            var loadingRequest = UnityEngine.Networking.UnityWebRequest.Get(inputAssetsPath);
            loadingRequest.SendWebRequest();

            while (!loadingRequest.isDone)
            {
                if (loadingRequest.isNetworkError || loadingRequest.isHttpError)
                {
                    throw new System.IO.IOException($"Downloading file '{inputAssetsPath}' from Streaming Assets failed with code {loadingRequest.responseCode}");
                }
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            System.IO.File.WriteAllBytes(outputFilePath, loadingRequest.downloadHandler.data);
        }
    }
}