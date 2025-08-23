Shader "Custom/AlwaysOnTopSprite"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Color Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        // ��������b�Ҧ� Geometry ����ø�s�A
        // �P�ɤ����`�׼v�T (ZTest Always)�C
        Tags { 
            "Queue"="Overlay"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
        }

        Pass
        {
            ZWrite Off       // ���n�g�J�`��
            ZTest Always     // �����`���˴��A�`�Oø�s
            Cull Off         // ������
            Blend SrcAlpha OneMinusSrcAlpha  // �z���V�X

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;

            v2f vert (appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv = TRANSFORM_TEX(IN.texcoord, _MainTex);
                // �N���I������ * �A�b���褤�]�w���C��
                OUT.color = IN.color * _Color;
                return OUT;
            }

            float4 frag (v2f IN) : SV_Target
            {
                float4 c = tex2D(_MainTex, IN.uv) * IN.color;
                return c;
            }
            ENDCG
        }
    }
}
