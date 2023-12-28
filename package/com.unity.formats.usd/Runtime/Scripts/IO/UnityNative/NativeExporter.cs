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
                ComponentToUsd(comp, objContext.path, exportContext.scene);
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
        static void ComponentToUsd(Component component, string path, Scene scene)
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

                LODGroup lodgroup = (LODGroup)component;
                string lod_prefix = "unity:LODGroup:lod";
                var lods = lodgroup.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    var attrName = new pxr.TfToken(lod_prefix + i.ToString());
                    var lod_targets = lods[i].renderers.Select(renderer => renderer.gameObject); // multiple game object per LOD
                    var targets_name = lod_targets.Select(target => target.name).ToArray();

                    pxr.VtStringArray usd_value = new pxr.VtStringArray((uint)targets_name.Length,"");
                    for (int j = 0; j < targets_name.Length; j++) usd_value[j] = targets_name[j];
                    prim.CreateAttribute(attrName, SdfValueTypeNames.StringArray).Set(usd_value);
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
namespace Unity.Formats.USD {
  public class NativeExporter {
    public static void ExportObject(ObjectContext objContext, ExportContext exportContext) {}
  }
}
#endif
