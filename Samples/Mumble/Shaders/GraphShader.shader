Shader "Unlit/GraphShader"
{
	SubShader
	{
		Pass
		{
			Blend Off 
			ZWrite Off 
			Cull Off
			Fog { Mode Off }
			BindChannels{
				Bind "vertex", vertex Bind "color", color
			}
		}
	}
}
