using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace FbxGenerator.Rendering
{
    public class StandardMaterial : Material
    {
        public Vector3 Albedo { get; set; } = Vector3.One;
        public float Metallic { get; set; } = 0.0f;
        public float Roughness { get; set; } = 0.5f;
        public float AO { get; set; } = 1.0f;
        public Texture? AlbedoTexture { get; set; }
        public Texture? MetallicTexture { get; set; }
        public Texture? RoughnessTexture { get; set; }
        public Texture? NormalTexture { get; set; }
        public Texture? AOTexture { get; set; }

        public override void Apply(ShaderProgram shaderProgram)
        {
            shaderProgram.SetVector3("material.albedo", Albedo);
            shaderProgram.SetFloat("material.metallic", Metallic);
            shaderProgram.SetFloat("material.roughness", Roughness);
            shaderProgram.SetFloat("material.ao", AO);

            int textureUnit = 0;

            if (AlbedoTexture != null)
            {
                AlbedoTexture.Bind(textureUnit);
                shaderProgram.SetInt("material.albedoMap", textureUnit++);
                shaderProgram.SetInt("hasAlbedoMap", 1);
            }
            else
            {
                shaderProgram.SetInt("hasAlbedoMap", 0);
            }

            if (MetallicTexture != null)
            {
                MetallicTexture.Bind(textureUnit);
                shaderProgram.SetInt("material.metallicMap", textureUnit++);
                shaderProgram.SetInt("hasMetallicMap", 1);
            }
            else
            {
                shaderProgram.SetInt("hasMetallicMap", 0);
            }

            if (RoughnessTexture != null)
            {
                RoughnessTexture.Bind(textureUnit);
                shaderProgram.SetInt("material.roughnessMap", textureUnit++);
                shaderProgram.SetInt("hasRoughnessMap", 1);
            }
            else
            {
                shaderProgram.SetInt("hasRoughnessMap", 0);
            }

            if (NormalTexture != null)
            {
                NormalTexture.Bind(textureUnit);
                shaderProgram.SetInt("material.normalMap", textureUnit++);
                shaderProgram.SetInt("hasNormalMap", 1);
            }
            else
            {
                shaderProgram.SetInt("hasNormalMap", 0);
            }

            if (AOTexture != null)
            {
                AOTexture.Bind(textureUnit);
                shaderProgram.SetInt("material.aoMap", textureUnit);
                shaderProgram.SetInt("hasAOMap", 1);
            }
            else
            {
                shaderProgram.SetInt("hasAOMap", 0);
            }
        }
    }
}