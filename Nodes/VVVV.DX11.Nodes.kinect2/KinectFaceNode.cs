﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VVVV.PluginInterfaces.V2;
using SlimDX;
using VVVV.MSKinect.Lib;
using VVVV.PluginInterfaces.V1;
using Microsoft.Kinect;
using Vector4 = SlimDX.Vector4;
using Quaternion = SlimDX.Quaternion;
using Microsoft.Kinect.Face;

namespace VVVV.MSKinect.Nodes
{
    [PluginInfo(Name = "Face", 
	            Category = "Kinect2", 
	            Version = "Microsoft", 
	            Author = "flateric", 
	            Tags = "DX11", 
	            Help = "Returns face data for each tracked user")]
    public class KinectFaceNode : IPluginEvaluate, IPluginConnections
    {
        [Input("Kinect Runtime")]
        private Pin<KinectRuntime> FInRuntime;

        [Output("Position Infrared")]
        private ISpread<Vector2> FOutPositionInfrared;

        [Output("Size Infrared")]
        private ISpread<Vector2> FOutSizeInfrared;

        [Output("Position Color")]
        private ISpread<Vector2> FOutPositionColor;

        [Output("Size Color")]
        private ISpread<Vector2> FOutSizeColor;

        [Output("Orientation")]
        private ISpread<Quaternion> FOutOrientation;

        [Output("Mouth Open")]
        private ISpread<DetectionResult> FOutMouthOpen;

        [Output("Frame Number", IsSingle = true)]
        private ISpread<int> FOutFrameNumber;

        private bool FInvalidateConnect = false;

        private KinectRuntime runtime;

        private FaceFrameSource[] faceFrameSources = null;
        private FaceFrameReader[] faceFrameReaders = null;
        private FaceFrameResult[] lastResults = null;

        private bool FInvalidate = false;

        private Body[] lastframe = new Body[6];

        private object m_lock = new object();
        private int frameid = -1;

        public KinectFaceNode()
        {
            faceFrameReaders = new FaceFrameReader[6];
            faceFrameSources = new FaceFrameSource[6];
            lastResults = new FaceFrameResult[6];
        }

        private int GetIndex(FaceFrameSource src)
        {
            for (int i = 0; i < faceFrameSources.Length;i++)
            {
                if (src == faceFrameSources[i]) { return i; }
            }
            return 0;
        }

        public void Evaluate(int SpreadMax)
        {
            if (this.FInvalidateConnect)
            {
                if (this.FInRuntime.PluginIO.IsConnected)
                {
                    //Cache runtime node
                    this.runtime = this.FInRuntime[0];

                    this.runtime.SkeletonFrameReady += SkeletonReady;

                    if (runtime != null)
                    {
                        FaceFrameFeatures faceFrameFeatures =
                            FaceFrameFeatures.BoundingBoxInColorSpace
                            | FaceFrameFeatures.PointsInColorSpace
                            | FaceFrameFeatures.RotationOrientation
                            | FaceFrameFeatures.FaceEngagement
                            | FaceFrameFeatures.Glasses
                            | FaceFrameFeatures.Happy
                            | FaceFrameFeatures.LeftEyeClosed
                            | FaceFrameFeatures.RightEyeClosed
                            | FaceFrameFeatures.LookingAway
                            | FaceFrameFeatures.MouthMoved
                            | FaceFrameFeatures.MouthOpen;

                        for (int i = 0; i < this.faceFrameSources.Length; i++)
                        {
                            this.faceFrameSources[i] = new FaceFrameSource(this.runtime.Runtime, 0, faceFrameFeatures);
                            this.faceFrameReaders[i] = this.faceFrameSources[i].OpenReader();
                            this.faceFrameReaders[i].FrameArrived += this.faceReader_FrameArrived;
                        }
                    }
                }
                else
                {
                    this.runtime.SkeletonFrameReady -= SkeletonReady;
                    for (int i = 0; i < this.faceFrameSources.Length; i++)
                    {
                        this.faceFrameReaders[i].FrameArrived -= this.faceReader_FrameArrived;
                        this.faceFrameReaders[i].Dispose();
                        this.faceFrameSources[i].Dispose();
                    }
                }

                this.FInvalidateConnect = false;
            }
        }

        void faceReader_FrameArrived(object sender, Microsoft.Kinect.Face.FaceFrameArrivedEventArgs e)
        {
            using (FaceFrame frame = e.FrameReference.AcquireFrame())
            {
                if (frame != null)
                {
                    var res = frame.FaceFrameResult;
                    if(res != null)
                    {
                        this.FOutFrameNumber[0] = (int)frame.FaceFrameResult.RelativeTime.Ticks;

                        Vector2 pos;
                        Vector2 size;

                        size.X = res.FaceBoundingBoxInColorSpace.Right - res.FaceBoundingBoxInColorSpace.Left;
                        //size.X /= 1920.0f;

                        size.Y = res.FaceBoundingBoxInColorSpace.Bottom - res.FaceBoundingBoxInColorSpace.Top;
                        //size.Y /= 1080.0f;

                        pos.X = size.X / 2.0f + (float)res.FaceBoundingBoxInColorSpace.Left;
                        pos.Y = size.Y / 2.0f + (float)res.FaceBoundingBoxInColorSpace.Top;

                        this.FOutPositionColor[0] = pos;
                        this.FOutSizeColor[0] = size;
                        
                        this.FOutOrientation[0] = new Quaternion(res.FaceRotationQuaternion.X, res.FaceRotationQuaternion.Y, 
                            res.FaceRotationQuaternion.Z, res.FaceRotationQuaternion.W);

                        this.FOutMouthOpen[0] = res.FaceProperties[FaceProperty.MouthOpen];
                    } 
                }
            }
        }


        private void SkeletonReady(object sender, BodyFrameArrivedEventArgs e)
        {
            using (BodyFrame skeletonFrame = e.FrameReference.AcquireFrame())
            {
                if (skeletonFrame != null)
                {
                   // lock (m_lock)
                   // {
                   skeletonFrame.GetAndRefreshBodyData(this.lastframe);
                   // }

                    for (int i = 0; i < this.lastResults.Length;i++)
                    {
                        if (this.faceFrameSources[i].IsTrackingIdValid)
                        {

                        }
                        else
                        {
                            if (this.lastframe[i].IsTracked)
                            {
                                this.faceFrameSources[i].TrackingId = this.lastframe[i].TrackingId;
                            }
                        }
                    }

                    skeletonFrame.Dispose();
                }
            }
            this.FInvalidate = true;
        }


        public void ConnectPin(IPluginIO pin)
        {
            if (pin == this.FInRuntime.PluginIO)
            {
                this.FInvalidateConnect = true;
            }
        }

        public void DisconnectPin(IPluginIO pin)
        {
            if (pin == this.FInRuntime.PluginIO)
            {
                this.FInvalidateConnect = true;
            }
        }
    }
}
