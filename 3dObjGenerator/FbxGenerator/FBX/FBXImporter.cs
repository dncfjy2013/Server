using FbxGenerator.Rendering;
using System.Collections.Generic;

namespace FbxGenerator.FBX
{
    public class FBXImporter
    {
        public SceneData Import(string filePath)
        {
            // 这里应该使用FBX SDK加载文件
            // 由于示例限制，我们返回一个空的SceneData
            return new SceneData
            {
                Meshes = new List<MeshData>(),
                Materials = new List<MaterialData>(),
                Textures = new List<TextureData>(),
                Animations = new List<AnimationData>(),
                Skeletons = new List<SkeletonData>()
            };
        }
    }

    public class SceneData
    {
        public List<MeshData> Meshes { get; set; }
        public List<MaterialData> Materials { get; set; }
        public List<TextureData> Textures { get; set; }
        public List<AnimationData> Animations { get; set; }
        public List<SkeletonData> Skeletons { get; set; }
    }

    public class MeshData
    {
        public string Name { get; set; }
        public List<Vertex> Vertices { get; set; }
        public List<uint> Indices { get; set; }
        public int MaterialIndex { get; set; }
    }

    public class MaterialData
    {
        public string Name { get; set; }
        public int DiffuseTextureIndex { get; set; }
        public int NormalTextureIndex { get; set; }
        public int SpecularTextureIndex { get; set; }
    }

    public class TextureData
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
    }

    public class AnimationData
    {
        public string Name { get; set; }
        public float Duration { get; set; }
        public List<BoneAnimationData> BoneAnimations { get; set; }
    }

    public class BoneAnimationData
    {
        public string BoneName { get; set; }
        public List<KeyframeData> Keyframes { get; set; }
    }

    public class KeyframeData
    {
        public float Time { get; set; }
        public OpenTK.Mathematics.Matrix4 Transform { get; set; }
    }

    public class SkeletonData
    {
        public string Name { get; set; }
        public List<BoneData> Bones { get; set; }
    }

    public class BoneData
    {
        public string Name { get; set; }
        public int ParentIndex { get; set; }
        public OpenTK.Mathematics.Matrix4 BindPose { get; set; }
    }
}