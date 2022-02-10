using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

namespace PretiaArCloud
{
    public delegate void SharedAnchorStateChanged(SharedAnchorState newState);
    internal delegate void SharedAnchorPoseUpdated(Pose sharedAnchorPose);
    public delegate void SharedAnchorSubsystemException(Exception e);
    public delegate void SharedAnchorScoreUpdated(float score);
    public delegate void SharedAnchorKeypointsUpdated(float[] keypoints);

    public sealed partial class SharedAnchorSubsystem : ISubsystem
    {
        internal class Factory
        {
            public static SharedAnchorSubsystem Create(PretiaSDKProjectSettings sdkSettings)
            {
                XRSessionSubsystem sessionSubsystem = null;
                XRCameraSubsystem cameraSubsystem = null;
                XRAnchorSubsystem anchorSubsystem = null;
                if (Utility.IsEditor() || Utility.IsStandalone())
                {
                    sessionSubsystem = new MockSessionSubsystem();
                    cameraSubsystem = new MockCameraSubsystem();
                    anchorSubsystem = new MockAnchorSubsystem();
                }
                else
                {
                    if (!XRGeneralSettings.Instance.TryGetSubsystem<XRSessionSubsystem>(out sessionSubsystem))
                    {
                        throw new NullReferenceException(nameof(sessionSubsystem));
                    }
                    if (!XRGeneralSettings.Instance.TryGetSubsystem<XRCameraSubsystem>(out cameraSubsystem))
                    {
                        throw new NullReferenceException(nameof(cameraSubsystem));
                    }
                    if (!XRGeneralSettings.Instance.TryGetSubsystem<XRAnchorSubsystem>(out anchorSubsystem))
                    {
                        throw new NullReferenceException(nameof(anchorSubsystem));
                    }
                }

                var scaledCameraManager = new ScaledCameraManager(cameraSubsystem);
                var nativeApi = new NativeApi();
                return new SharedAnchorSubsystem(
                    sessionSubsystem: sessionSubsystem,
                    
                    anchorSubsystem: anchorSubsystem,

                    localMapRelocalizer: new LocalMapRelocalizer(
                        scaledCameraManager,
                        nativeApi),

                    cloudMapRelocalizer: new CloudMapRelocalizer(
                        sdkSettings.RelocServerAddress,
                        sdkSettings.RelocServerPort,
                        sdkSettings.AppKey,
                        scaledCameraManager,
                        nativeApi),

                    imageRelocalizer: new ImageRelocalizer()
                );
            }
        }

        private const int CONSECUTIVE_SUCCESSFUL_RELOC_TARGET = 3;
        private const int FAILED_ATTEMPT_BEFORE_COOLDOWN = 10;
        private const float COOLDOWN_TIME_IN_SECOND = 1f;
        private const float MAX_DISTANCE_DELTA = .2f;
        private const float MAX_ANGLE_DELTA = 30f;

        private XRSessionSubsystem _sessionSubsystem;
        private XRAnchorSubsystem _anchorSubsystem;
        private LocalMapRelocalizer _localMapRelocalizer;
        private CloudMapRelocalizer _cloudMapRelocalizer;
        private ImageRelocalizer _imageRelocalizer;

        private Transform _cameraTransform;
        private XRAnchor _cameraAnchor = XRAnchor.defaultValue;
        private XRAnchor _sharedAnchor = XRAnchor.defaultValue;
        private List<XRAnchor> _relocAnchors = new List<XRAnchor>(CONSECUTIVE_SUCCESSFUL_RELOC_TARGET);
        private IRelocalizer _activeRelocalizer;

        private bool _isRunning = false;
        private Task _previousRelocalizationTask;
        private Task _relocalizationTask;
        private CancellationTokenSource _relocalizationCancelSource;

        public Pose SharedAnchorPose => _sharedAnchor.pose;
        public float RelocScore { get; private set; } = 0.0f;

        public SharedAnchorState CurrentState { get; private set; } = SharedAnchorState.Stopped;

        public event SharedAnchorStateChanged OnSharedAnchorStateChanged;
        internal event SharedAnchorPoseUpdated OnSharedAnchorPoseUpdated;
        public event SharedAnchorSubsystemException OnException;
        public event SharedAnchorScoreUpdated OnSharedAnchorScoreUpdated;
        public event SharedAnchorKeypointsUpdated OnSharedAnchorKeypointsUpdated;

        internal enum SharedAnchorTypeChoice
        {
            Image,
            CloudMap,
            LocalMap,
        }

        internal SharedAnchorSubsystem(
            XRSessionSubsystem sessionSubsystem,
            XRAnchorSubsystem anchorSubsystem,
            LocalMapRelocalizer localMapRelocalizer,
            CloudMapRelocalizer cloudMapRelocalizer,
            ImageRelocalizer imageRelocalizer) : base()
        {
            _sessionSubsystem = sessionSubsystem;
            _anchorSubsystem = anchorSubsystem;
            _localMapRelocalizer = localMapRelocalizer;
            _cloudMapRelocalizer = cloudMapRelocalizer;
            _imageRelocalizer = imageRelocalizer;
        }

        internal void SetCameraTransform(Transform cameraTransform)
        {
            _cameraTransform = cameraTransform;
        }

        internal void SetMapKey(string mapKey)
        {
            _cloudMapRelocalizer.SetMapKey(mapKey);
        }

        internal void SetTrackedImageManager(ARTrackedImageManager trackedImageManager)
        {
            _imageRelocalizer.SetTrackedImageManager(trackedImageManager);
        }

        internal void SetReferenceImage(XRReferenceImage referenceImage)
        {
            _imageRelocalizer.SetReferenceImage(referenceImage);
        }

        public void Initialize()
        {
            
        }

        public void Resume()
        {
            _isRunning = true;
        }

        internal void StartRelocalization(SharedAnchorTypeChoice sharedAnchorType, int timeoutInMilliseconds)
        {
            // Reset if already started
            if (CurrentState != SharedAnchorState.Stopped)
            {
                Reset();
            }
            
            // Prepare the task
            if (timeoutInMilliseconds == 0)
            {
                _relocalizationCancelSource = new CancellationTokenSource();
            }
            else
            {
                _relocalizationCancelSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutInMilliseconds));
            }
            _relocalizationTask = Run(sharedAnchorType, _relocalizationCancelSource.Token);

            _relocalizationTask.ContinueWith(
                _ => ResetInternal(),
                cancellationToken: default,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());

            _relocalizationTask.ContinueWith(
                t => {
                    Debug.LogException(t.Exception);
                    OnException?.Invoke(t.Exception.InnerException);
                },
                cancellationToken: default,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        private async Task Run(
            SharedAnchorTypeChoice sharedAnchorType,
            CancellationToken cancellationToken = default)
        {
            if (_previousRelocalizationTask != null && _previousRelocalizationTask.Status == TaskStatus.Running)
            {
                await _previousRelocalizationTask;
            }

            switch (sharedAnchorType)
            {
                case SharedAnchorTypeChoice.CloudMap:
                    _activeRelocalizer = _cloudMapRelocalizer;
                    await RunMapRelocalizationAsync(cancellationToken);
                break;

                case SharedAnchorTypeChoice.LocalMap:
                    _activeRelocalizer = _localMapRelocalizer;
                    await RunMapRelocalizationAsync(cancellationToken);
                break;

                case SharedAnchorTypeChoice.Image:
                    _activeRelocalizer = _imageRelocalizer;
                    await RunImageRelocalizationAsync(cancellationToken);
                break;
            }

            await MonitorSharedAnchor(cancellationToken);
        }

        private async Task RunMapRelocalizationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Initialize relocalization (including map loading)

                TransitionToState(SharedAnchorState.Initializing);

                _anchorSubsystem.Start();

                await _activeRelocalizer.InitializeAsync(cancellationToken);
                await WaitForCameraTransformAsync();

                // Start relocalizing

                TransitionToState(SharedAnchorState.Relocalizing);
                int consecutiveSuccessfulReloc = 0;
                int failedAttempt = 0;
                Pose cameraPose = default;
                Queue<float> scores = new Queue<float>(CONSECUTIVE_SUCCESSFUL_RELOC_TARGET);

                // Check for success criteria
                while (consecutiveSuccessfulReloc < CONSECUTIVE_SUCCESSFUL_RELOC_TARGET)
                {
                    await WaitWhilePausedAsync(cancellationToken);

                    // Check that the AR session is tracking correctly this frame
                    if (_sessionSubsystem.trackingState != TrackingState.Tracking)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                        continue;
                    }

                    // Get the camera pose in the AR coordinate system (localPosition / localRotation)
                    cameraPose.position = _cameraTransform.localPosition;
                    cameraPose.rotation = _cameraTransform.localRotation;

                    // Save the camera position as an anchor
                    if (!_anchorSubsystem.running)
                    {
                        throw new Exception("Anchor subsystem not running");
                    }

                    if (!TrySetAnchor(ref _cameraAnchor, cameraPose))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                        continue;
                    }

                    // If failure criteria has been triggered
                    if (failedAttempt >= FAILED_ATTEMPT_BEFORE_COOLDOWN)
                    {
                        await WaitForCooldownAsync(cancellationToken);
                        failedAttempt = 0;
                        continue;
                    }

                    // Try to relocalize
                    var (status, result) = await _activeRelocalizer.RelocalizeAsync(cancellationToken);

                    // Compute score (moving average)
                    scores.Enqueue(result.Score);
                    if (scores.Count > CONSECUTIVE_SUCCESSFUL_RELOC_TARGET)
                    {
                        scores.Dequeue();
                    }
                    float scoreAverage = 0.0f;
                    foreach (var s in scores) { scoreAverage += s; }
                    scoreAverage /= scores.Count;
                    RelocScore = scoreAverage;

                    // Compute the shared anchor pose
                    Pose sharedAnchorPose = Utility.EstimateSharedAnchor(result.Pose, _cameraAnchor.pose);

                    // Create an anchor at the shared anchor pose
                    if (_anchorSubsystem.TryAddAnchor(sharedAnchorPose, out XRAnchor relocAnchor))
                    {
                        _relocAnchors.Add(relocAnchor);
                    }
                    else
                    {
                        status = RelocState.Lost;
                    }

                    // Save the result
                    if (status == RelocState.Tracking)
                    {
                        consecutiveSuccessfulReloc++;
                    }
                    else
                    {
                        CleanupRelocAnchors();
                        consecutiveSuccessfulReloc = 0;
                        failedAttempt++;
                    }

                    OnSharedAnchorScoreUpdated?.Invoke(RelocScore);
                    OnSharedAnchorKeypointsUpdated?.Invoke(result.Keypoints);

                } // while not enough consecutive successful reloc



                bool anchorCreationOk = false;

                while (!anchorCreationOk)
                {
                    // Compute the final shared anchor pose
                    Pose sharedAnchorPose = Utility.GetAveragePose(_relocAnchors);
                    sharedAnchorPose = Utility.SetPoseUpward(sharedAnchorPose);

                    // Create an anchor to represent the shared anchor position
                    anchorCreationOk = TrySetAnchor(ref _sharedAnchor, sharedAnchorPose);

                    if (!anchorCreationOk)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                }


                
                OnSharedAnchorPoseUpdated?.Invoke(_sharedAnchor.pose);
                TransitionToState(SharedAnchorState.Relocalized);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                MapRelocalizationCleanup();
            }
        }

        private void CleanupRelocAnchors()
        {
            foreach (var anchor in _relocAnchors)
            {
                _anchorSubsystem.TryRemoveAnchor(anchor.trackableId);
            }
            _relocAnchors.Clear();
        }

        private void MapRelocalizationCleanup()
        {
            TryRemoveAnchor(ref _cameraAnchor);
            CleanupRelocAnchors();
            _activeRelocalizer.Cleanup();
        }

        private bool TryRemoveAnchor(ref XRAnchor anchor)
        {
            if (anchor.trackableId != XRAnchor.defaultValue.trackableId)
            {
                if (!_anchorSubsystem.TryRemoveAnchor(anchor.trackableId))
                {
                    return false;
                }
            }
            anchor = XRAnchor.defaultValue;
            return true;
        }

        private bool TrySetAnchor(ref XRAnchor anchor, Pose pose)
        {
            if (!TryRemoveAnchor(ref anchor)) { return false; }
            return _anchorSubsystem.TryAddAnchor(pose, out anchor);
        }

        private bool TryAttachAnchor(ref XRAnchor anchor, Pose pose, TrackableId trackableId)
        {
            if (!TryRemoveAnchor(ref anchor)) { return false; }
            return _anchorSubsystem.TryAttachAnchor(trackableId, pose, out anchor);
        }

        private async Task WaitForCameraTransformAsync(CancellationToken cancellationToken = default)
        {
            while (_cameraTransform == null)
                await Task.Yield();
        }

        private async Task WaitWhilePausedAsync(CancellationToken cancellationToken = default)
        {
            while (!_isRunning)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private async Task WaitForCooldownAsync(CancellationToken cancellationToken = default)
        {
            float startTime = Time.unscaledTime;
            while (Time.unscaledTime - startTime < COOLDOWN_TIME_IN_SECOND)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cameraInitialPose = _cameraAnchor.pose;

                float distance = Vector3.Distance(cameraInitialPose.position, _cameraTransform.localPosition);
                if (distance >= MAX_DISTANCE_DELTA)
                    return;

                float angle = Quaternion.Angle(cameraInitialPose.rotation, _cameraTransform.localRotation);
                if (angle >= MAX_ANGLE_DELTA)
                    return;

                await Task.Yield();
            }
        }

        private async Task RunImageRelocalizationAsync(CancellationToken cancellationToken = default)
        {
            // Initialize
            TransitionToState(SharedAnchorState.Initializing);

            await _activeRelocalizer.InitializeAsync(cancellationToken);

            // Run relocalization
            TransitionToState(SharedAnchorState.Relocalizing);

            RelocState status = RelocState.Lost;
            RelocResult result = default;

            while (status != RelocState.Tracking)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await WaitWhilePausedAsync(cancellationToken);

                // Check that the AR session is tracking correctly this frame
                if (_sessionSubsystem.trackingState != TrackingState.Tracking)
                {
                    await Task.Yield();
                    continue;
                }

                (status, result) = await _activeRelocalizer.RelocalizeAsync(cancellationToken);
                RelocScore = result.Score;
                
                OnSharedAnchorScoreUpdated?.Invoke(RelocScore);
                OnSharedAnchorKeypointsUpdated?.Invoke(null);

                // Create the shared anchor
                if (status == RelocState.Tracking)
                {
                    if (!TrySetAnchor(ref _sharedAnchor, Utility.SetPoseUpward(result.Pose)))
                    {
                        status = RelocState.Lost;
                    }
                }
                
                if (status != RelocState.Tracking)
                {
                    await Task.Yield();
                }
            }

            OnSharedAnchorPoseUpdated?.Invoke(_sharedAnchor.pose);
            TransitionToState(SharedAnchorState.Relocalized);
        }

        private async Task MonitorSharedAnchor(CancellationToken cancellationToken = default)
        {
            while (CurrentState == SharedAnchorState.Relocalized)
            {
                await WaitWhilePausedAsync(cancellationToken);

                // Wait for n seconds
                float startTime = Time.unscaledTime;
                while (Time.unscaledTime - startTime < 1.0f)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                if (_sessionSubsystem.trackingState != TrackingState.Tracking)
                {
                    continue;
                }

                if (_sharedAnchor.trackingState == TrackingState.Tracking)
                {
                    // Signal to update the shared anchor
                    OnSharedAnchorPoseUpdated?.Invoke(_sharedAnchor.pose);
                }
                else
                {
                    // The shared anchor is not tracking, back to relocalizing until it's tracked again
                    // Note: this does not seem to happen on Android
                    TransitionToState(SharedAnchorState.Relocalizing);
                    while (_sharedAnchor.trackingState != TrackingState.Tracking)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                    OnSharedAnchorPoseUpdated?.Invoke(_sharedAnchor.pose);
                    TransitionToState(SharedAnchorState.Relocalized);
                }
            }
        }

        private void TransitionToState(SharedAnchorState newState)
        {
            if (CurrentState == newState) return;

            CurrentState = newState;

            OnSharedAnchorStateChanged?.Invoke(CurrentState);
        }

        public void Pause()
        {
            _isRunning = false;
        }

        private void CancelRelocalization()
        {
            if (_relocalizationCancelSource != null &&
                !_relocalizationCancelSource.IsCancellationRequested)
            {
                _relocalizationCancelSource.Cancel();
            }
        }

        private void ResetInternal()
        {
            if (_activeRelocalizer != null)
            {
                _activeRelocalizer.Reset();
            }
            TryRemoveAnchor(ref _sharedAnchor);
            RelocScore = 0.0f;
            TransitionToState(SharedAnchorState.Stopped);
        }

        public void Reset()
        {
            if (_relocalizationTask == null)
            {
                ResetInternal();
            }
            else
            {
                _previousRelocalizationTask = _relocalizationTask;
                CancelRelocalization();
            }
        }

        public void Destroy()
        {
            Reset();

            _localMapRelocalizer.Dispose();
            _cloudMapRelocalizer.Dispose();
            _imageRelocalizer.Dispose();
        }
    }
}