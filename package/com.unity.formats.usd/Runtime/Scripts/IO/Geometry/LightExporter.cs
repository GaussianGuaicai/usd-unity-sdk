// Copyright 2018 Jeremy Cowles. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEngine;
using USD.NET;
using USD.NET.Unity;

namespace Unity.Formats.USD
{
    public static class LightExporter
    {

        public static void ExportXform(ObjectContext objContext, ExportContext exportContext)
        {
            UnityEngine.Profiling.Profiler.BeginSample("USD: Xform Conversion");

            XformSample sample = (XformSample)objContext.sample;
            var localRot = objContext.gameObject.transform.localRotation;
            var localScale = objContext.gameObject.transform.localScale;
            var path = new pxr.SdfPath(objContext.path);
            var prim = exportContext.scene.GetPrimAtPath(path);

            // If exporting for Z-Up, rotate the world.
            bool correctZUp = exportContext.scene.UpAxis == Scene.UpAxes.Z;

            sample.transform = XformExporter.GetLocalTransformMatrix(
                objContext.gameObject.transform,
                correctZUp,
                path.IsRootPrimPath(),
                exportContext.basisTransform);

            UnityEngine.Profiling.Profiler.EndSample();

            UnityEngine.Profiling.Profiler.BeginSample("USD: Xform Write");
            exportContext.scene.Write(objContext.path, sample);
            UnityEngine.Profiling.Profiler.EndSample();
        }
    }
}
