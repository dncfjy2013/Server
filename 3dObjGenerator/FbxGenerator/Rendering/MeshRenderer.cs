using FbxGenerator.Engine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace FbxGenerator.Rendering
{
    public class MeshRenderer : Component
    {
        public Mesh Mesh { get; set; } = null!;
        public Material Material { get; set; } = null!;

        public override void Initialize()
        {
            base.Initialize();

            if (Mesh == null)
                Mesh = MeshFactory.CreateCube();

            if (Material == null)
                Material = new StandardMaterial();
        }

        public void Render(ShaderProgram shaderProgram, Transform transform)
        {
            // 设置材质属性
            Material.Apply(shaderProgram);

            // 设置模型矩阵
            shaderProgram.SetMatrix4("model", transform.WorldMatrix);

            // 绘制网格
            Mesh.Render();
        }
    }
}