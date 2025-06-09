using OpenTK.Mathematics;
using StlGenerator.Core;
using StlGenerator.Models;
using StlGenerator.Processing;
using static StlGenerator.ShapeGeneratorFactory;

namespace StlGenerator.Processing
{
    /// <summary>
    /// 汽车模型生成器
    /// </summary>
    public class CarModel
    {
        // 汽车尺寸参数
        private readonly float _length;     // 汽车长度(X方向)
        private readonly float _width;      // 汽车宽度(Z方向)
        private readonly float _height;     // 汽车高度(Y方向)
        private readonly float _wheelRadius; // 车轮半径
        private readonly float _wheelWidth;  // 车轮宽度
        
        // 汽车结构比例参数
        private readonly float _bodyRatio;   // 车身高度占比
        private readonly float _roofRatio;   // 车顶高度占比
        private readonly float _wheelBase;   // 轴距占比
        
        public CarModel(
            float length = 2.0f, 
            float width = 0.8f, 
            float height = 0.6f,
            float wheelRadius = 0.15f,
            float wheelWidth = 0.1f,
            float bodyRatio = 0.6f,
            float roofRatio = 0.4f,
            float wheelBase = 0.7f)
        {
            _length = length;
            _width = width;
            _height = height;
            _wheelRadius = wheelRadius;
            _wheelWidth = wheelWidth;
            _bodyRatio = bodyRatio;
            _roofRatio = roofRatio;
            _wheelBase = wheelBase;
        }
        
        /// <summary>
        /// 生成汽车模型
        /// </summary>
        public Model GenerateModel()
        {
            ModelMerger merger = new ModelMerger();
            
            // 1. 计算基本尺寸
            float bodyHeight = _height * _bodyRatio;
            float roofHeight = _height * _roofRatio;
            float wheelBaseLength = _length * _wheelBase;
            
            // 2. 生成车身
            GenerateBody(merger, bodyHeight, roofHeight);
            
            // 3. 生成车轮
            GenerateWheels(merger);
            
            // 4. 生成车窗和车灯（可选）
            // GenerateDetails(merger, bodyHeight);
            
            return merger.Merge();
        }
        
        /// <summary>
        /// 生成车身和车顶
        /// </summary>
        private void GenerateBody(ModelMerger merger, float bodyHeight, float roofHeight)
        {
            // 车身主体
            Model bodyModel = CreateGenerator(
                ShapeType.Cuboid,
                _length,                // X方向长度
                bodyHeight,            // Y方向高度
                _width                 // Z方向宽度
            ).GenerateModel();
            
            // 车顶（简化为缩小的长方体）
            Model roofModel = CreateGenerator(
                ShapeType.Cuboid,
                _length * 0.8f,        // 车顶长度略短
                roofHeight,            // 车顶高度
                _width * 0.8f          // 车顶宽度略窄
            ).GenerateModel();
            
            // 添加车身（位置在原点）
            merger.AddModel(bodyModel, Vector3.Zero, Quaternion.Identity, Vector3.One);
            
            // 添加车顶（位于车身上方）
            Vector3 roofPosition = new Vector3(0, bodyHeight, 0);
            merger.AddModel(roofModel, roofPosition, Quaternion.Identity, Vector3.One);
        }
        
        /// <summary>
        /// 生成四个车轮
        /// </summary>
        private void GenerateWheels(ModelMerger merger)
        {
            // 计算车轮位置
            float wheelBaseLength = _length * _wheelBase;
            float frontWheelX = wheelBaseLength / 2;
            float rearWheelX = -wheelBaseLength / 2;
            float wheelZ = (_width + _wheelWidth) / 2;
            float wheelY = _wheelRadius;  // 车轮底部与地面接触
            
            // 创建车轮模型
            Model wheelModel = CreateGenerator(
                ShapeType.Cylinder,
                _wheelRadius,           // 车轮半径
                _wheelWidth,            // 车轮宽度
                32                      // 分段数
            ).GenerateModel();

            // 车轮朝向：Z轴为旋转轴
            Quaternion wheelRotation = Quaternion.FromAxisAngle(Vector3.UnitZ, MathHelper.DegreesToRadians(90));
            
            // 前左轮
            Vector3 frontLeftPos = new Vector3(frontWheelX, wheelY, wheelZ);
            merger.AddModel(wheelModel, frontLeftPos, wheelRotation, Vector3.One);
            
            // 前右轮
            Vector3 frontRightPos = new Vector3(frontWheelX, wheelY, -wheelZ);
            merger.AddModel(wheelModel, frontRightPos, wheelRotation, Vector3.One);
            
            // 后左轮
            Vector3 rearLeftPos = new Vector3(rearWheelX, wheelY, wheelZ);
            merger.AddModel(wheelModel, rearLeftPos, wheelRotation, Vector3.One);
            
            // 后右轮
            Vector3 rearRightPos = new Vector3(rearWheelX, wheelY, -wheelZ);
            merger.AddModel(wheelModel, rearRightPos, wheelRotation, Vector3.One);
        }
        
        /// <summary>
        /// 生成汽车细节（车窗、车灯等）
        /// </summary>
        private void GenerateDetails(ModelMerger merger, float bodyHeight)
        {
            // 简化实现，实际应用中可扩展
            // 车窗、车灯等可以通过添加小型几何体实现
        }
    }
}