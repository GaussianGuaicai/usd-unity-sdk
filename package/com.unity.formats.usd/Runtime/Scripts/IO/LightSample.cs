using pxr;
using UnityEngine;

namespace USD.NET.Unity
{
	[System.Serializable]
	public class LightSampleBase : XformSample
	{
		public LightSampleBase()
		{
		}

		public virtual void CopyFromLight(UnityEngine.Light light, bool convertTransformToUsd = true)
		{
			var tr = light.transform;
			transform = UnityEngine.Matrix4x4.TRS(tr.localPosition,
                tr.localRotation,
                tr.localScale);
            if (convertTransformToUsd)
            {
                ConvertTransform();
            }
		}

		public virtual void CopyToLight(UnityEngine.Light light, UsdPrim usdPrim, bool setTransform, bool is_spot_light = false)
		{
			if (setTransform)
            {
                var tr = light.transform;
                var xf = transform;
                UnityTypeConverter.SetTransform(xf, tr);
            }

            // UsdStageWeakPtr stage = usdPrim.GetStage();
            // SdfPath path = usdPrim.GetPrimPath();
			// UsdLuxLight lightAPI = UsdLuxLight.Get(stage,path);

            // Light Intensity
            light.intensity = (float)usdPrim.GetAttribute(new TfToken("inputs:intensity")).Get(); // lightAPI.GetIntensityAttr().Get();
			if(is_spot_light) light.intensity *= .1f;

			// Light Color
			GfVec3f color = usdPrim.GetAttribute(new TfToken("inputs:color")).Get(); // lightAPI.GetColorAttr().Get();
			light.color = new Color(color[0],color[1],color[2]);

			// Light Shadows
			UsdAttribute attr_shadow = usdPrim.GetAttribute(new TfToken("inputs:shadow:enable"));
			int shadow = attr_shadow.IsValid() ? (int)attr_shadow.Get() : 1;// (bool)UsdLuxShadowAPI.Get(stage,path).GetShadowEnableAttr().Get();
            light.shadows = shadow==1 ? LightShadows.Soft : LightShadows.None;
		}
	}

	[System.Serializable]
	[UsdSchema("DistantLight")]
	public class DistantLightSample : LightSampleBase
	{
		// Core Light parameters
		public float angle;
		public float intensity;

		public DistantLightSample()
		{
		}

		public DistantLightSample(UnityEngine.Light fromLight)
		{
			CopyFromLight(fromLight);
		}

		override public void CopyFromLight(UnityEngine.Light light, bool convertTransformToUsd = true)
		{
			intensity = light.intensity;
			base.CopyFromLight(light, convertTransformToUsd);
		}

		override public void CopyToLight(UnityEngine.Light light, UsdPrim usdPrim, bool setTransform, bool is_spot_light = false)
		{
            // UsdStageWeakPtr stage = usdPrim.GetStage();
            // var usdDistantLight = UsdLuxDistantLight.Get(stage, usdPrim.GetPrimPath());
			light.type = LightType.Directional;
			light.shadowAngle = (float)usdPrim.GetAttribute(new TfToken("inputs:angle")).Get();// (float)usdDistantLight.GetAngleAttr().Get();
			base.CopyToLight(light, usdPrim, setTransform);
		}
	}

	[System.Serializable]
	[UsdSchema("SphereLight")]
	public class SphereLightSample : LightSampleBase
	{
		// Core Light parameters
		public bool treatAsPoint;
		public float radius;

		[UsdNamespace("shaping:cone")]
		public float angle;

		public SphereLightSample()
		{
		}

		public SphereLightSample(UnityEngine.Light fromLight)
		{
			CopyFromLight(fromLight);
		}

		override public void CopyFromLight(UnityEngine.Light light, bool convertTransformToUsd = true)
		{
			treatAsPoint = true;
			radius = light.range;
			if (light.spotAngle > 0)
			{
				angle = light.spotAngle;
			}
			base.CopyFromLight(light, convertTransformToUsd);
		}

		override public void CopyToLight(UnityEngine.Light light, UsdPrim usdPrim, bool setTransform, bool is_spot_light = false)
		{
            UsdAttribute attr_cone_angle = usdPrim.GetAttribute(new TfToken("inputs:shaping:cone:angle"));
			if(attr_cone_angle.IsValid())
			{
				light.type = LightType.Spot;
				float cone_angle = (float)attr_cone_angle.Get();
				light.spotAngle = angle = cone_angle*2;
				base.CopyToLight(light, usdPrim, setTransform,true);
			}
			else
			{
				light.type = LightType.Point;
				base.CopyToLight(light, usdPrim, setTransform);
			}
		}
	}

	[System.Serializable]
	[UsdSchema("RectLight")]
	public class RectLightSample : LightSampleBase
	{
		public RectLightSample()
		{
		}

		public RectLightSample(UnityEngine.Light fromLight)
		{
			base.CopyFromLight(fromLight);
		}

		override public void CopyToLight(UnityEngine.Light light, UsdPrim usdPrim, bool setTransform, bool is_spot_light = false)
		{
			light.type = LightType.Rectangle;
			float width = (float)usdPrim.GetAttribute(new TfToken("inputs:width")).Get();
			float height = (float)usdPrim.GetAttribute(new TfToken("inputs:height")).Get();
			light.areaSize = new Vector2(width,height);
			base.CopyToLight(light, usdPrim, setTransform);
		}
	}

	[System.Serializable]
	[UsdSchema("DiskLight")]
	public class DiskLightSample : LightSampleBase
	{
		public DiskLightSample()
		{
		}

		public DiskLightSample(UnityEngine.Light fromLight)
		{
			base.CopyFromLight(fromLight);
		}

		override public void CopyToLight(UnityEngine.Light light, UsdPrim usdPrim, bool setTransform, bool is_spot_light = false)
		{
			light.type = LightType.Disc;
			base.CopyToLight(light, usdPrim, setTransform);
		}
	}
}