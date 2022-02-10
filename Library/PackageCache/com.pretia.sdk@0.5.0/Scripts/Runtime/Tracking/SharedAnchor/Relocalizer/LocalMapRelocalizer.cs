using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PretiaArCloud
{
    using static SharedAnchorSubsystem;

    internal class LocalMapRelocalizer : IRelocalizer
    {
        private readonly IScaledCameraManager _scaledCameraManager;
        private readonly SharedAnchorSubsystem.INativeApi _nativeApi;

        private string _mapPath;
        private string _vocabPath;

        public LocalMapRelocalizer(
            IScaledCameraManager scaledCameraManager,
            SharedAnchorSubsystem.INativeApi nativeApi)
        {
            _scaledCameraManager = scaledCameraManager;
            _nativeApi = nativeApi;

            _mapPath = Path.Combine(Application.streamingAssetsPath, "map.msg").ToString();
            _vocabPath = Path.Combine(Application.streamingAssetsPath, "orb_vocab.dbow2").ToString();
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
#if UNITY_ANDROID
            if (_vocabPath.StartsWith(Application.streamingAssetsPath))
            {
                var outputVocabPath = Path.Combine(Application.persistentDataPath, "orb_vocab.dbow2").ToString();
                await Utility.StreamingAssetsToFile(_vocabPath, outputVocabPath, cancellationToken);
                _vocabPath = outputVocabPath;
            }
            if (_mapPath.StartsWith(Application.streamingAssetsPath))
            {
                var outputMapPath = Path.Combine(Application.persistentDataPath, "map.msg").ToString();
                await Utility.StreamingAssetsToFile(_mapPath, outputMapPath, cancellationToken);
                _mapPath = outputMapPath;
            }
#endif

            if (!File.Exists(_vocabPath))
            {
                throw new ArgumentException($"Vocabulary file not found at {_vocabPath}");
            }

            if (!File.Exists(_mapPath))
            {
                throw new ArgumentException($"Map file not found at {_mapPath}");
            }

            await InitializeSlamAsync(cancellationToken);

            var status = await Task<StatusCode>.Run(() => _nativeApi.LoadMap(_mapPath));

            if (status != StatusCode.SUCCESS)
            {
                throw new Exception($"Local map loading failed with {status}");
            }
        }

        public async Task<(RelocState, RelocResult)> RelocalizeAsync(CancellationToken cancellationToken = default)
        {
            // Get camera image
            if (!_scaledCameraManager.TryGetLatestCameraTexture(out var cameraTexture, out var timestamp))
            {
                return (RelocState.Initializing, default(RelocResult));
            }

            long timestampNano = (long)(timestamp * 1e9);
            
            IntPtr rawData = _scaledCameraManager.GetTextureUnsafePtr(cameraTexture);
            return await Task.Run(() => {
                int frameStatus = 0;
                float[] slamPose = new float[7];

                var status = _nativeApi.RelocalizeFrame(rawData, timestampNano, ref frameStatus, ref slamPose);
                if (status != StatusCode.SUCCESS)
                {
                    throw new Exception($"Frame relocalization failed with {status}");
                }

                RelocState relocStatus = (frameStatus == 0 ? RelocState.Tracking : RelocState.Lost);
                RelocResult relocResult = default;

                if (status == StatusCode.SUCCESS && relocStatus == RelocState.Tracking)
                {
                    relocResult.Pose.rotation.x = slamPose[0];
                    relocResult.Pose.rotation.y = slamPose[1];
                    relocResult.Pose.rotation.z = slamPose[2];
                    relocResult.Pose.rotation.w = slamPose[3];
                    relocResult.Pose.position.x = slamPose[4];
                    relocResult.Pose.position.y = slamPose[5];
                    relocResult.Pose.position.z = slamPose[6];
                }

                status = _nativeApi.GetRelocalizationScore(ref relocResult.Score);
                if (status != StatusCode.SUCCESS)
                {
                    Debug.LogError($"Unable to fetch relocalization score: {status}");
                }

                status = _nativeApi.GetRelocalizationKeypoints(out relocResult.Keypoints);
                if (status != StatusCode.SUCCESS)
                {
                    Debug.LogError($"Unable to fetch relocalization keypoints: {status}");
                }

                return (relocStatus, relocResult);
            });
        }

        private async Task InitializeSlamAsync(CancellationToken cancellationToken = default)
        {
            Vector2 focalLength;
            Vector2 principalPoint;
            Vector2Int resolution;
            while (!_scaledCameraManager.TryGetIntrinsics(
                out focalLength,
                out principalPoint,
                out resolution))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            await Task.Run(() =>
            {
                var status = _nativeApi.InitializeSlam(
                    vocabPath: _vocabPath,
                    deviceName: Utility.GetDeviceName(),
                    fx: focalLength.x, fy: focalLength.y,
                    cx: principalPoint.x, cy: principalPoint.y,
                    width: resolution.x, height: resolution.y);

                if (status != StatusCode.SUCCESS)
                {
                    throw new Exception($"Native initialization of local map failed with {status}");
                }
            });
        }

        public void Cleanup()
        {
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}