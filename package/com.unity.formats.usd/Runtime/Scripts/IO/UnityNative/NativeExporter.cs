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

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using USD.NET;
using pxr;
using USD.NET.Unity;
#if UNITY_EDITOR
using UnityEditor;

namespace Unity.Formats.USD
{
    public class NativeExporter
    {
        // -------------------------------------------------------------------------------------------- //
        // Serialize Unity to -> USD
        // -------------------------------------------------------------------------------------------- //

        /// <summary>
        /// Exports the given game object to USD, via Unity SerializedObject.
        /// Note that this is an experimental work in progress.
        /// </summary>
        public static void ExportObject(ObjectContext objContext,
            ExportContext exportContext)
        {
            if (!exportContext.exportNative)
            {
                return;
            }

            var prim = exportContext.scene.GetPrimAtPath(objContext.path);
            ObjectToUsd(objContext.gameObject, prim, exportContext.scene);
            foreach (Component comp in objContext.gameObject.GetComponents(typeof(Component)))
            {
                ComponentToUsd(comp, objContext.path, exportContext.scene, exportContext);
            }
        }

        /// <summary>
        /// Exports a single GameObject to USD, does not export components.
        /// </summary>
        static void ObjectToUsd(GameObject gameObj, pxr.UsdPrim prim, Scene scene)
        {
            var obj = new SerializedObject(gameObj);
            var sb = new System.Text.StringBuilder();
            var path = prim.GetPath().ToString();
            sb.AppendLine("Visited: " + path);

      prim.SetCustomDataByKey(new pxr.TfToken("unity:name"), new pxr.TfToken(gameObj.name));

            // Gaussian: Add Unity Hierarchy path
            var gameObj_root = gameObj.transform.root.gameObject;
            string path_relto_root = GetHierarchyPath(gameObj, gameObj.transform.root.gameObject);
            prim.SetCustomDataByKey(new pxr.TfToken("unity:path"), new pxr.TfToken(path_relto_root));

            var itr = obj.GetIterator();
            itr.Next(true);
            PropertyToUsd(path, "", scene, itr, sb);
        }

        /// <summary>
        /// Exports a single component to USD, does not include the parent GameObject.
        /// </summary>
        static void ComponentToUsd(Component component, string path, Scene scene, ExportContext exportContext)
        {
            var obj = new SerializedObject(component);
            var sb = new System.Text.StringBuilder();
            var propPrefix = component.GetType().Name;

            sb.AppendLine("Visited: " + path + "." + propPrefix);

            var itr = obj.GetIterator();
            itr.Next(true);
            PropertyToUsd(path, propPrefix, scene, itr, sb);

            if (propPrefix == "LODGroup")
            {
                var primPath = new pxr.SdfPath(path);
                var prim = scene.GetPrimAtPath(primPath);

                Dictionary<string,List<GameObject>> lods_info = new Dictionary<string, List<GameObject>>();
                pxr.UsdVariantSet variantSet = null;
                string first_variantName = "";
                if(ExportOptional.lODGroupsAsVariantSets)
                {
                    variantSet = prim.GetVariantSet("lods");
                    if (!variantSet.IsValid())
                    {
                        variantSet = prim.GetVariantSets().AddVariantSet("lods");
                    }
                }

                LODGroup lodgroup = (LODGroup)component;
                string lod_prefix = "unity:LODGroup:lod";
                var lods = lodgroup.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    string lod_suffix = i.ToString();
                    var attrName = new TfToken(lod_prefix + lod_suffix);

                    List<GameObject> lod_targets = new List<GameObject>();
                    foreach (var renderer in lods[i].renderers)
                    {
                        if(renderer==null)
                        {
                            Debug.LogWarningFormat(lodgroup,"{0} LOD Group contains invalid LOD renderers.",lodgroup.gameObject);
                            continue;
                        }
                        lod_targets.Add(renderer.gameObject);
                    }
                    var targets_name = lod_targets.Select(target => target.name).ToArray();

                    // Add variant for each LODs
                    if(ExportOptional.lODGroupsAsVariantSets)
                    {
                        var variantName = lod_suffix;
                        variantSet.AddVariant(variantName);
                        lods_info.Add(variantName, lod_targets);
                        if(i==0) first_variantName = variantName;
                    }

                    pxr.VtStringArray usd_value = new pxr.VtStringArray((uint)targets_name.Length,"");
                    for (int j = 0; j < targets_name.Length; j++) usd_value[j] = targets_name[j];
                    
                    UsdAttribute attrib_lod = prim.CreateAttribute(attrName, SdfValueTypeNames.StringArray);
                    attrib_lod.Set(usd_value);
                }

                // Variant Editing
                if (ExportOptional.lODGroupsAsVariantSets)
                {
                    var srcEditTarget = scene.Stage.GetEditTargetForLocalLayer(scene.Stage.GetRootLayer());
                    foreach (var lod_info in lods_info)
                    {
                        var variantName = lod_info.Key;
                        var variantGameObjects = lod_info.Value;

                        var other_lods_info = lods_info.Where(info => info.Key != lod_info.Key);
                        var other_lods_gos = other_lods_info.SelectMany(kv => kv.Value).Except(variantGameObjects); //it may contain duplicates from current variantGameObjects, just remove it.

                        variantSet.SetVariantSelection(variantName);
                        var editTarget = variantSet.GetVariantEditTarget();
                        foreach (var other_go in other_lods_gos)
                        {
                            if (!exportContext.plans.TryGetValue(other_go, out var plan))
                            {
                                Debug.LogError("USD LOD Export: No plan to export " + other_go, other_go);
                                continue;
                            }
                            if (plan.exporters.Count() == 0)
                            {
                                Debug.LogError("USD LOD Export: No exporter for " + other_go, other_go);
                                continue;
                            }

                            var other_primpath = new SdfPath(plan.exporters.First().path);
                            var other_prim = scene.Stage.DefinePrim(other_primpath); // make sure is defined even its not exist
                            if (other_prim == null)
                            {
                                Debug.LogError("USD LOD Export: No prim for " + other_primpath, other_go);
                                continue;
                            }

                            // LODs object have to be under the same hierarchy
                            if(other_primpath.GetCommonPrefix(primPath)!=primPath)
                            {
                                Debug.LogError("USD LOD Export: LODs are not under the same hierarchy " + other_primpath, other_go);
                                continue;
                            }

                            // ==== Variant Editing =====
                            exportContext.lodVeriantsPrimPaths.Add(other_primpath); // this will clear its local opinion later
                            scene.Stage.SetEditTarget(editTarget);
                            var other_imageable = new UsdGeomImageable(other_prim);
                            other_imageable.MakeInvisible();
                            scene.Stage.SetEditTarget(srcEditTarget);
                            // =========================
                        }
                    }
                    variantSet.SetVariantSelection(first_variantName);
                }
            }
            else if (component is LightProbeGroup)
            {
                var primPath = new SdfPath(path);
                var prim = scene.GetPrimAtPath(primPath);

                LightProbeGroup lightProbeGroup = (LightProbeGroup)component;
                string probeGroup_prefix = "unity:LightProbeGroup:probePositions";
                var probes = lightProbeGroup.probePositions;
                VtVec3fArray usd_value = new VtVec3fArray((uint)probes.Count());
                for (int i = 0; i < probes.Count(); i++)
                {
                    var correctProbe = UnityTypeConverter.ChangeBasis(probes[i]);
                    usd_value[i] = new GfVec3f(correctProbe.x, correctProbe.y, correctProbe.z);
                }
                var attribProbePosition = prim.CreateAttribute(new TfToken(probeGroup_prefix),SdfValueTypeNames.Float3Array);
                attribProbePosition.Set(usd_value);
                
                if(ExportOptional.lightProbesAsPoints)
                {
                    UsdGeomPoints usdGeomPoints = UsdGeomPoints.Define(prim.GetStage(),primPath);
                    usdGeomPoints.CreatePointsAttr().Set(usd_value);
                }
            }


            //Debug.Log(sb.ToString());

            // TODO: Handle multiple components of the same type.
            var usdPrim = scene.Stage.GetPrimAtPath(new pxr.SdfPath(path));
            var attr = usdPrim.CreateAttribute(
                new pxr.TfToken("unity:component:" + component.GetType().Name + ":type"),
                SdfValueTypeNames.String);

            attr.Set(component.GetType().AssemblyQualifiedName);
        }


        /// <summary>
        /// Writes SerializedProperty to USD, traversing all nested properties.
        /// </summary>
        static void PropertyToUsd(string path,
            string propPrefix,
            Scene scene,
            SerializedProperty prop,
            System.Text.StringBuilder sb)
        {
            string prefix = "";
            try
            {
                var nameStack = new List<string>();
                nameStack.Add("unity");
                if (!string.IsNullOrEmpty(propPrefix))
                {
                    nameStack.Add(propPrefix);
                }

                string lastName = "";
                int lastDepth = 0;

                while (prop.Next(prop.propertyType == SerializedPropertyType.Generic && !prop.isArray))
                {
                    string tabIn = "";
                    for (int i = 0; i < prop.depth; i++)
                    {
                        tabIn += "  ";
                    }

                    if (prop.depth > lastDepth)
                    {
                        Debug.Assert(lastName != "");
                        nameStack.Add(lastName);
                    }
                    else if (prop.depth < lastDepth)
                    {
                        nameStack.RemoveRange(nameStack.Count - (lastDepth - prop.depth), lastDepth - prop.depth);
                    }

                    lastDepth = prop.depth;
                    lastName = prop.name;

                    if (nameStack.Count > 0)
                    {
                        prefix = string.Join(":", nameStack.ToArray());
                        prefix += ":";
                    }
                    else
                    {
                        prefix = "";
                    }

                    sb.Append(tabIn + prefix + prop.name + "[" + prop.propertyType.ToString() + "] = ");
                    if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
                    {
                        // TODO.
                        sb.AppendLine("ARRAY");
                    }
                    else if (prop.propertyType == SerializedPropertyType.Generic)
                    {
                        sb.AppendLine("Generic");
                    }
                    else if (prop.propertyType == SerializedPropertyType.AnimationCurve ||
                             prop.propertyType == SerializedPropertyType.Gradient)
                    {
                        // TODO.
                        sb.AppendLine(NativeSerialization.ValueToString(prop));
                    }
                    else
                    {
                        sb.AppendLine(NativeSerialization.ValueToString(prop));
                        var vtValue = NativeSerialization.PropToVtValue(prop);
                        var primPath = new pxr.SdfPath(path);
                        var attrName = new pxr.TfToken(prefix + prop.name);
                        /*
                        var oldPrim = context.prevScene.Stage.GetPrimAtPath(primPath);
                        pxr.VtValue oldVtValue = null;
                        if (oldPrim.IsValid()) {
                          var oldAttr = oldPrim.GetAttribute(attrName);
                          if (oldAttr.IsValid()) {
                            oldVtValue = oldAttr.Get(0);
                          }
                        }

                        if (oldVtValue != null && vtValue == oldVtValue) {
                          Debug.Log("skipping: " + prop.name);
                          continue;
                        }
                        */

                        var sdfType = NativeSerialization.GetSdfType(prop);
                        var prim = scene.GetPrimAtPath(primPath);
                        var attr = prim.CreateAttribute(attrName, sdfType);
                        attr.Set(vtValue);
                    }
                }
            }
            catch
            {
                Debug.LogWarning("Failed on: " + path + "." + prefix + prop.name);
                throw;
            }
        }
      static string GetHierarchyPath(GameObject target_gameobject, GameObject root_gameobject)
    {
        // Get hierarchy path relative to root
        var target_hier_transforms = target_gameobject.GetComponentsInParent<Transform>();
        var root_hier_transforms = target_hier_transforms.TakeWhile(tf => tf.IsChildOf(root_gameobject.transform));
        var rel_hier_transforms = root_hier_transforms.Take(root_hier_transforms.Count() - 1);
        string rel_target_hier_path = string.Join("/", root_hier_transforms.Select(t => t.name).Reverse().ToArray());
        return rel_target_hier_path;
    }

    }
}
#else
namespace Unity.Formats.USD
{
    public class NativeExporter
    {
        public static void ExportObject(ObjectContext objContext, ExportContext exportContext) { }
    }
}
#endif
