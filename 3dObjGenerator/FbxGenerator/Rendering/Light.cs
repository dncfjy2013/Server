using FbxGenerator.Engine;
using OpenTK.Mathematics;

namespace FbxGenerator.Rendering
{
    public enum LightType
    {
        Directional,
        Point,
        Spot
    }

    public class Light : Component
    {
        public LightType Type { get; set; } = LightType.Point;
        public Vector3 Color { get; set; } = Vector3.One;
        public float Intensity { get; set; } = 1.0f;

        // 点光源和聚光灯属性
        public float Constant { get; set; } = 1.0f;
        public float Linear { get; set; } = 0.09f;
        public float Quadratic { get; set; } = 0.032f;

        // 聚光灯属性
        public float Cutoff { get; set; } = (float)MathHelper.Cos(MathHelper.DegreesToRadians(12.5f));
        public float OuterCutoff { get; set; } = (float)MathHelper.Cos(MathHelper.DegreesToRadians(15.0f));

        public void SetLightUniforms(ShaderProgram shaderProgram, int lightIndex)
        {
            var lightName = $"lights[{lightIndex}]";

            shaderProgram.SetInt($"{lightName}.type", (int)Type);
            shaderProgram.SetVector3($"{lightName}.position", GameObject.Transform.Position);
            shaderProgram.SetVector3($"{lightName}.direction",  GameObject.Transform.Forward);
            shaderProgram.SetVector3($"{lightName}.color", Color * Intensity);

            shaderProgram.SetFloat($"{lightName}.constant", Constant);
            shaderProgram.SetFloat($"{lightName}.linear", Linear);
            shaderProgram.SetFloat($"{lightName}.quadratic", Quadratic);

            shaderProgram.SetFloat($"{lightName}.cutoff", Cutoff);
            shaderProgram.SetFloat($"{lightName}.outerCutoff", OuterCutoff);
        }
    }
}