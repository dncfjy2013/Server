using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoordinateSystem
{
    /// <summary>
    /// 坐标系类型枚举
    /// 用于区分系统中不同的坐标系，支持层级父子关系
    /// </summary>
    public enum CoordinateSystemType
    {
        Stage,      // 位移台实际坐标系
        World,      // 理想坐标系
        RotStage,   // Z轴旋转坐标系
        Wafer,      // 偏移+旋转坐标系
        Offset,     // 偏移坐标系
    }

    /// <summary>
    /// 长度单位枚举
    /// 系统基准单位：微米（Um）
    /// 支持从纳米到米的全尺寸单位自动换算
    /// </summary>
    public enum LengthUnit
    {
        /// <summary>
        /// 纳米 
        /// </summary>
        Nm,

        /// <summary>
        /// 微米
        /// </summary>
        Um,

        /// <summary>
        /// 毫米
        /// </summary>
        Mm,

        /// <summary>
        /// 厘米
        /// </summary>
        Cm,

        /// <summary>
        /// 米
        /// </summary>
        M
    }
}