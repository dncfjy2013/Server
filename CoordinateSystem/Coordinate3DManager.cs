using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CoordinateSystem
{
    /// <summary>
    /// 三维坐标系管理器（单例模式）
    /// 负责：坐标系注册、获取、坐标转换、批量转换、单位转换、配置保存与加载
    /// 线程安全，支持多线程并发访问
    /// </summary>
    public class Coordinate3DManager
    {
        /// <summary>
        /// 单例实例（线程安全模式）
        /// </summary>
        private static readonly Lazy<Coordinate3DManager> _inst =
            new(() => new Coordinate3DManager(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 获取全局唯一的坐标系管理器实例
        /// </summary>
        public static Coordinate3DManager Instance => _inst.Value;

        /// <summary>
        /// 系统级变更事件（任意坐标系参数改变时触发）
        /// </summary>
        public event EventHandler<CoordinateChangedEventArgs> SystemChanged;

        /// <summary>
        /// 坐标系字典（按类型存储所有坐标系）
        /// </summary>
        private readonly Dictionary<CoordinateSystemType, CoordinateSystem3D> _systems = new();

        /// <summary>
        /// 线程锁对象，保证多线程安全访问
        /// </summary>
        private readonly object _lockObj = new object();

        /// <summary>
        /// 私有构造函数（单例禁止外部实例化）
        /// 初始化时自动构建默认坐标系
        /// </summary>
        private Coordinate3DManager()
        {
            BuildDefault();
        }

        /// <summary>
        /// 构建【严格链式坐标系】
        /// 结构：Stage → World → RotStage → Wafer → Offset
        /// </summary>
        private void BuildDefault()
        {
            var stage = new CoordinateSystem3D(CoordinateSystemType.Stage)
            {
                Parent = null,
                ParentType = null,
            };

            var world = new CoordinateSystem3D(CoordinateSystemType.World)
            {
                Parent = stage,
                ParentType = CoordinateSystemType.Stage,
            };

            var rotStage = new CoordinateSystem3D(CoordinateSystemType.RotStage)
            {
                Parent = world,
                ParentType = CoordinateSystemType.World,
            };

            var wafer = new CoordinateSystem3D(CoordinateSystemType.Wafer)
            {
                Parent = rotStage,
                ParentType = CoordinateSystemType.RotStage,
            };

            var offset = new CoordinateSystem3D(CoordinateSystemType.Offset)
            {
                Parent = wafer,
                ParentType = CoordinateSystemType.Wafer,
            };

            Register(stage);
            Register(world);
            Register(rotStage);
            Register(wafer);
            Register(offset);
        }

        /// <summary>
        /// 注册一个坐标系到管理器
        /// 自动绑定变更事件，线程安全
        /// </summary>
        /// <param name="sys">要注册的坐标系</param>
        public void Register(CoordinateSystem3D sys)
        {
            if (sys == null) return;

            lock (_lockObj)
            {
                _systems[sys.Type] = sys;
                // 子系统变更 → 触发系统总变更
                sys.Changed += (s, e) => SystemChanged?.Invoke(this, e);
            }
        }

        /// <summary>
        /// 根据类型获取坐标系实例
        /// </summary>
        /// <param name="type">坐标系类型</param>
        /// <returns>对应的3D坐标系实例</returns>
        /// <exception cref="KeyNotFoundException">未找到时抛出</exception>
        public CoordinateSystem3D Get(CoordinateSystemType type)
        {
            lock (_lockObj)
            {
                if (_systems.TryGetValue(type, out var s))
                    return s;

                throw new KeyNotFoundException($"未找到坐标系:{type}");
            }
        }

        /// <summary>
        /// 坐标转换：从源坐标系 → 目标坐标系
        /// 内部通过世界坐标系作为中转
        /// </summary>
        /// <param name="p">待转换点</param>
        /// <param name="from">源坐标系</param>
        /// <param name="to">目标坐标系</param>
        /// <returns>转换后点</returns>
        public Point3D Convert(Point3D p, CoordinateSystemType from, CoordinateSystemType to)
        {
            var src = Get(from);
            var dst = Get(to);
            var world = src.ConvertToWorld(p);
            return dst.ConvertFromWorld(world);
        }

        /// <summary>
        /// 批量坐标转换（一组点）
        /// 自带异常捕获与日志记录
        /// </summary>
        public List<Point3D> ConvertRange(IEnumerable<Point3D> list, CoordinateSystemType from, CoordinateSystemType to)
        {
            try
            {
                return list.Select(p => Convert(p, from, to)).ToList();
            }
            catch (Exception ex)
            {
                CoordLogger.Error($"批量转换失败 {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 带目标单位的坐标转换
        /// 自动完成单位换算（如 μm → nm / mm / m）
        /// </summary>
        public Point3D ConvertWithUnit(Point3D p, CoordinateSystemType from, CoordinateSystemType to, LengthUnit targetUnit)
        {
            var res = Convert(p, from, to);
            double scale = CoordMath.GetUnitScale(Get(to).Unit, targetUnit);
            return new Point3D(res.X * scale, res.Y * scale, res.Z * scale);
        }

        /// <summary>
        /// 将所有坐标系配置保存为 JSON 文件
        /// 线程安全，自动格式化输出
        /// </summary>
        /// <param name="filePath">保存路径</param>
        /// <returns>生成的JSON字符串</returns>
        public string SaveToJson(string filePath)
        {
            lock (_lockObj)
            {
                var list = _systems.Values.ToList();
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
                CoordLogger.Info($"已保存 {list.Count} 个坐标系 到：{filePath}");
                return json;
            }
        }

        /// <summary>
        /// 从 JSON 文件加载坐标系配置
        /// 自动重建父子引用关系，恢复所有参数与层级结构
        /// 自带异常处理、文件存在性检查
        /// </summary>
        /// <param name="filePath">配置文件路径</param>
        public void LoadFromJson(string filePath)
        {
            lock (_lockObj)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        CoordLogger.Error($"文件不存在：{filePath}");
                        return;
                    }

                    var json = File.ReadAllText(filePath);
                    var list = JsonSerializer.Deserialize<List<CoordinateSystem3D>>(json);

                    if (list == null)
                    {
                        CoordLogger.Error("配置为空");
                        return;
                    }

                    // 清空并重新加载所有坐标系
                    _systems.Clear();
                    foreach (var s in list)
                        _systems[s.Type] = s;

                    // 重建父子引用关系
                    foreach (var s in list)
                        if (s.ParentType.HasValue && _systems.ContainsKey(s.ParentType.Value))
                            s.Parent = _systems[s.ParentType.Value];

                    CoordLogger.Info($"已从 {filePath} 加载 {list.Count} 个坐标系");
                }
                catch (Exception ex)
                {
                    CoordLogger.Error(ex.Message);
                }
            }
        }

        #region 坐标系之间相对变换更新（新增：平移/旋转/缩放）
        /// <summary>
        /// 对【目标坐标系】执行【相对自身】的增量平移
        /// 不会影响基准坐标系
        /// </summary>
        public void TranslateRelative(CoordinateSystemType target, double dx, double dy, double dz)
        {
            var sys = Get(target);
            sys.Translate(dx, dy, dz);
            RefreshAllCoordinateMatrices();
        }

        /// <summary>
        /// 对【目标坐标系】执行【相对自身】的增量旋转（欧拉角 ZYX）
        /// </summary>
        public void RotateRelativeEuler(CoordinateSystemType target, double drx, double dry, double drz)
        {
            var sys = Get(target);
            var current = sys.Rotation.ToEulerZyx();
            sys.SetRotationEuler(current.rx + drx, current.ry + dry, current.rz + drz);
            RefreshAllCoordinateMatrices();
        }

        /// <summary>
        /// 对【目标坐标系】执行【相对自身】的增量缩放
        /// </summary>
        public void ScaleRelative(CoordinateSystemType target, double dsx, double dsy, double dsz)
        {
            var sys = Get(target);
            var newScale = new Point3D(
                sys.Scale.X + dsx,
                sys.Scale.Y + dsy,
                sys.Scale.Z + dsz
            );
            sys.SetScale(newScale.X, newScale.Y, newScale.Z);
            RefreshAllCoordinateMatrices();
        }

        /// <summary>
        /// 对【目标坐标系】执行【相对自身】的等比例增量缩放
        /// </summary>
        public void ScaleUniformRelative(CoordinateSystemType target, double ds)
        {
            ScaleRelative(target, ds, ds, ds);
            RefreshAllCoordinateMatrices();
        }

        /// <summary>
        /// 直接设置【目标坐标系】的绝对偏移
        /// </summary>
        public void SetAbsoluteOffset(CoordinateSystemType target, double x, double y, double z)
        {
            var sys = Get(target);
            sys.SetOffset(x, y, z);
            RefreshAllCoordinateMatrices();
        }

        /// <summary>
        /// 直接设置【目标坐标系】的绝对旋转（欧拉角）
        /// </summary>
        public void SetAbsoluteRotation(CoordinateSystemType target, double rx, double ry, double rz)
        {
            var sys = Get(target);
            sys.SetRotationEuler(rx, ry, rz);
            RefreshAllCoordinateMatrices();
        }

        /// <summary>
        /// 直接设置【目标坐标系】的绝对缩放
        /// </summary>
        public void SetAbsoluteScale(CoordinateSystemType target, double sx, double sy, double sz)
        {
            var sys = Get(target);
            sys.SetScale(sx, sy, sz);
            RefreshAllCoordinateMatrices();
        }
        #endregion

        #region 坐标系相对另一个坐标系的变换更新（链式刷新）
        /// <summary>
        /// 让 target 坐标系 相对于 base坐标系 进行【增量平移】
        /// 并自动链式刷新转换关系
        /// </summary>
        public void TranslateRelativeTo(
            CoordinateSystemType target,
            CoordinateSystemType baseCoord,
            double dx, double dy, double dz)
        {
            lock (_lockObj)
            {
                var targetSys = Get(target);
                var baseSys = Get(baseCoord);

                // 将【基准坐标系下的增量】转换到【世界坐标系】再应用
                Point3D worldDelta = baseSys.ConvertToWorld(new Point3D(dx, dy, dz));
                Point3D baseWorld = baseSys.ConvertToWorld(new Point3D(0, 0, 0));
                Point3D finalDelta = new Point3D(
                    worldDelta.X - baseWorld.X,
                    worldDelta.Y - baseWorld.Y,
                    worldDelta.Z - baseWorld.Z
                );

                targetSys.Translate(finalDelta.X, finalDelta.Y, finalDelta.Z);
                RefreshAllCoordinateMatrices();
            }
        }

        /// <summary>
        /// 让 target 坐标系 相对于 base坐标系 进行【增量旋转】（欧拉角 ZYX）
        /// 并自动链式刷新转换关系
        /// </summary>
        public void RotateRelativeTo(
            CoordinateSystemType target,
            CoordinateSystemType baseCoord,
            double drx, double dry, double drz)
        {
            lock (_lockObj)
            {
                var targetSys = Get(target);
                var rot = targetSys.Rotation.ToEulerZyx();
                targetSys.SetRotationEuler(rot.rx + drx, rot.ry + dry, rot.rz + drz);
                RefreshAllCoordinateMatrices();
            }
        }

        /// <summary>
        /// 让 target 坐标系 相对于 base坐标系 进行【增量缩放】
        /// 并自动链式刷新转换关系
        /// </summary>
        public void ScaleRelativeTo(
            CoordinateSystemType target,
            CoordinateSystemType baseCoord,
            double dsx, double dsy, double dsz)
        {
            lock (_lockObj)
            {
                var targetSys = Get(target);
                var newScale = new Point3D(
                    targetSys.Scale.X + dsx,
                    targetSys.Scale.Y + dsy,
                    targetSys.Scale.Z + dsz
                );
                targetSys.SetScale(newScale.X, newScale.Y, newScale.Z);
                RefreshAllCoordinateMatrices();
            }
        }

        /// <summary>
        /// 【全局刷新】
        /// 强制所有坐标系重新计算变换矩阵 & 链式转换关系
        /// 用于修改位姿后保证转换完全正确
        /// </summary>
        public void RefreshAllCoordinateMatrices()
        {
            lock (_lockObj)
            {
                foreach (var sys in _systems.Values)
                {
                    sys.GetMatrix();
                }
                CoordLogger.Info("所有坐标系变换矩阵已链式刷新");
            }
        }
        #endregion

        #region 高级扩展管理功能 
        /// <summary>
        /// 创建并注册一个新坐标系
        /// </summary>
        public CoordinateSystem3D CreateCoordinate(CoordinateSystemType type, CoordinateSystemType? parentType = null)
        {
            lock (_lockObj)
            {
                if (_systems.ContainsKey(type))
                    throw new InvalidOperationException($"坐标系 {type} 已存在");

                var sys = new CoordinateSystem3D(type);
                if (parentType.HasValue && _systems.TryGetValue(parentType.Value, out var parent))
                {
                    sys.Parent = parent;
                    sys.ParentType = parentType;
                }
                Register(sys);
                CoordLogger.Warn($"创建坐标系: {type}, 父级: {parentType}");
                return sys;
            }
        }

        /// <summary>
        /// 删除一个坐标系
        /// </summary>
        public bool RemoveCoordinate(CoordinateSystemType type)
        {
            lock (_lockObj)
            {
                if (!_systems.ContainsKey(type)) return false;
                bool removed = _systems.Remove(type);
                if (removed)
                    CoordLogger.Warn($"已删除坐标系: {type}");
                return removed;
            }
        }

        /// <summary>
        /// 动态修改坐标系的父级（基准坐标系）
        /// </summary>
        public void SetParent(CoordinateSystemType child, CoordinateSystemType? parent)
        {
            lock (_lockObj)
            {
                var childSys = Get(child);
                if (parent.HasValue && _systems.TryGetValue(parent.Value, out var p))
                {
                    childSys.Parent = p;
                    childSys.ParentType = parent;
                }
                else
                {
                    childSys.Parent = null;
                    childSys.ParentType = null;
                }
                RefreshAllCoordinateMatrices();
                CoordLogger.Info($"已将 {child} 的父坐标系设为: {parent}");
            }
        }

        /// <summary>
        /// 获取从 source 到 target 的完整变换差：偏移、旋转、缩放
        /// </summary>
        public (Point3D offset, Point3D rotation, Point3D scale) GetTransformDelta(
            CoordinateSystemType source, CoordinateSystemType target)
        {
            lock (_lockObj)
            {
                var src = Get(source);
                var dst = Get(target);

                var worldZero = src.ConvertToWorld(new Point3D(0, 0, 0));
                var localZero = dst.ConvertFromWorld(worldZero);

                var srcRot = src.Rotation.ToEulerZyx();
                var dstRot = dst.Rotation.ToEulerZyx();
                var rotDelta = new Point3D(
                    dstRot.rx - srcRot.rx,
                    dstRot.ry - srcRot.ry,
                    dstRot.rz - srcRot.rz
                );

                var scaleDelta = new Point3D(
                    dst.Scale.X / src.Scale.X,
                    dst.Scale.Y / src.Scale.Y,
                    dst.Scale.Z / src.Scale.Z
                );

                return (localZero, rotDelta, scaleDelta);
            }
        }

        /// <summary>
        /// 将 source 坐标系对齐到 target 坐标系（自动设置偏移/旋转/缩放）
        /// </summary>
        public void AlignTo(CoordinateSystemType source, CoordinateSystemType target)
        {
            lock (_lockObj)
            {
                var (offset, rot, scale) = GetTransformDelta(target, source);
                var sys = Get(source);

                sys.SetOffset(offset.X, offset.Y, offset.Z);
                sys.SetRotationEuler(rot.X, rot.Y, rot.Z);
                sys.SetScale(scale.X, scale.Y, scale.Z);

                RefreshAllCoordinateMatrices();
                CoordLogger.Info($"坐标系 {source} 已对齐到 {target}");
            }
        }

        /// <summary>
        /// 重置单个坐标系为默认状态（零偏移、无旋转、单位缩放）
        /// </summary>
        private void ResetCoordinate(CoordinateSystemType type)
        {
            lock (_lockObj)
            {
                var sys = Get(type);
                sys.SetOffset(0, 0, 0);
                sys.SetRotationEuler(0, 0, 0);
                sys.SetScale(1, 1, 1);

                CoordLogger.Warn($"坐标系 {type} 已重置");
            }
        }

        /// <summary>
        /// 重置所有坐标系为默认状态
        /// </summary>
        public void ResetAll()
        {
            lock (_lockObj)
            {
                foreach (var type in _systems.Keys.ToList())
                    ResetCoordinate(type);
                RefreshAllCoordinateMatrices();
                CoordLogger.Info("所有坐标系已重置为默认状态");
            }
        }

        /// <summary>
        /// 获取所有已注册的坐标系类型
        /// </summary>
        public List<CoordinateSystemType> GetAllCoordinateTypes()
        {
            lock (_lockObj)
                return _systems.Keys.ToList();
        }

        /// <summary>
        /// 获取坐标系信息（调试/日志用）
        /// </summary>
        public string GetCoordinateInfo(CoordinateSystemType type)
        {
            lock (_lockObj)
            {
                var sys = Get(type);
                var rot = sys.Rotation.ToEulerZyx();
                return $"[{type}] 偏移:{sys.Offset} 旋转({rot.rx:F1},{rot.ry:F1},{rot.rz:F1}) 缩放:{sys.Scale} 单位:{sys.Unit} 父:{sys.ParentType}";
            }
        }

        /// <summary>
        /// 输出所有坐标系信息
        /// </summary>
        public List<string> GetAllCoordinateInfos()
        {
            lock (_lockObj)
            {
                var list = new List<string>();
                foreach (var t in _systems.Keys)
                    list.Add(GetCoordinateInfo(t));
                return list;
            }
        }
        #endregion

        #region ==================== 链式位移台坐标系管理 ====================

        // 获取各级坐标系
        public CoordinateSystem3D GetStage() => Get(CoordinateSystemType.Stage);
        public CoordinateSystem3D GetWorld() => Get(CoordinateSystemType.World);
        public CoordinateSystem3D GetRotStage() => Get(CoordinateSystemType.RotStage);
        public CoordinateSystem3D GetWafer() => Get(CoordinateSystemType.Wafer);
        public CoordinateSystem3D GetOffset() => Get(CoordinateSystemType.Offset);

        /// <summary>
        /// 设置位移台实际坐标（安装位置）
        /// </summary>
        public void SetStage(double x, double y, double z, double rx = 0, double ry = 0, double rz = 0)
        {
            lock (_lockObj)
            {
                var sys = GetStage();
                sys.SetOffset(x, y, z);
                sys.SetRotationEuler(rx, ry, rz);
                RefreshAllCoordinateMatrices();
            }
        }

        /// <summary>
        /// 设置理想坐标系（理想原点）
        /// </summary>
        public void SetWorld(double x, double y, double z)
        {
            lock (_lockObj)
            {
                GetWorld().SetOffset(x, y, z);
                RefreshAllCoordinateMatrices();
            }
        }

        /// <summary>
        /// 设置Z轴旋转（仅旋转Z）
        /// </summary>
        public void SetRotStage(double zAngleDeg)
        {
            lock (_lockObj)
            {
                GetRotStage().SetRotationEuler(0, 0, zAngleDeg);
                RefreshAllCoordinateMatrices();
            }
        }

        /// <summary>
        /// 设置Wafer：偏移 + Z旋转
        /// </summary>
        public void SetWafer(double x, double y, double z, double rz)
        {
            lock (_lockObj)
            {
                var sys = GetWafer();
                sys.SetOffset(x, y, z);
                sys.SetRotationEuler(0, 0, rz);
                RefreshAllCoordinateMatrices();
            }
        }

        /// <summary>
        /// 设置最终偏移坐标系
        /// </summary>
        public void SetOffset(double x, double y)
        {
            lock (_lockObj)
            {
                GetOffset().SetOffset(x, y, 0);
                RefreshAllCoordinateMatrices();
            }
        }

        /// <summary>
        /// 链式转换：任意子坐标系 → 位移台实际坐标
        /// </summary>
        public Point3D ConvertToStage(CoordinateSystemType from, Point3D p)
        {
            return Convert(p, from, CoordinateSystemType.Stage);
        }

        /// <summary>
        /// 链式转换：位移台坐标 → 任意子坐标系
        /// </summary>
        public Point3D ConvertFromStage(CoordinateSystemType to, Point3D p)
        {
            return Convert(p, CoordinateSystemType.Stage, to);
        }

        #endregion

        #region ==================== 核心：RotStage / Wafer / Offset 参数更新工具 ====================

        // ============================== RotStage (Z轴旋转坐标系) 更新 ==============================
        /// <summary>
        /// 更新 RotStage 的 Z轴旋转角度（最常用：角度补偿）
        /// </summary>
        public void UpdateRotStageRotation(double zAngleDeg)
        {
            lock (_lockObj)
            {
                var rot = GetRotStage();
                rot.SetRotationEuler(0, 0, zAngleDeg);
                RefreshAllCoordinateMatrices();
                CoordLogger.Info($"RotStage Z轴旋转已更新: {zAngleDeg:F2}°");
            }
        }

        /// <summary>
        /// 更新 RotStage 的偏移量
        /// </summary>
        public void UpdateRotStageOffset(double x, double y, double z)
        {
            lock (_lockObj)
            {
                var rot = GetRotStage();
                rot.SetOffset(x, y, z);
                RefreshAllCoordinateMatrices();
                CoordLogger.Info($"RotStage 偏移已更新: X={x:F2}, Y={y:F2}, Z={z:F2}");
            }
        }

        // ============================== Wafer (偏移+旋转坐标系) 更新 ==============================
        /// <summary>
        /// 更新 Wafer 坐标系：偏移 + Z轴旋转（标定核心参数）
        /// </summary>
        public void UpdateWaferTransform(double x, double y, double z, double zAngleDeg)
        {
            lock (_lockObj)
            {
                var wafer = GetWafer();
                wafer.SetOffset(x, y, z);
                wafer.SetRotationEuler(0, 0, zAngleDeg);
                RefreshAllCoordinateMatrices();
                CoordLogger.Info($"Wafer 参数已更新: 偏移({x:F2},{y:F2},{z:F2}), 旋转Z={zAngleDeg:F2}°");
            }
        }

        /// <summary>
        /// 单独更新 Wafer 偏移
        /// </summary>
        public void UpdateWaferOffset(double x, double y, double z)
        {
            lock (_lockObj)
            {
                GetWafer().SetOffset(x, y, z);
                RefreshAllCoordinateMatrices();
                CoordLogger.Info($"Wafer 偏移已更新");
            }
        }

        /// <summary>
        /// 单独更新 Wafer Z轴旋转
        /// </summary>
        public void UpdateWaferRotation(double zAngleDeg)
        {
            lock (_lockObj)
            {
                GetWafer().SetRotationEuler(0, 0, zAngleDeg);
                RefreshAllCoordinateMatrices();
                CoordLogger.Info($"Wafer Z旋转已更新: {zAngleDeg:F2}°");
            }
        }

        // ============================== Offset (最终偏移坐标系) 更新 ==============================
        /// <summary>
        /// 更新最终 Offset 偏移量（仅XY，常用于工位/位置微调）
        /// </summary>
        public void UpdateOffset(double x, double y)
        {
            lock (_lockObj)
            {
                var offset = GetOffset();
                offset.SetOffset(x, y, 0);
                RefreshAllCoordinateMatrices();
                CoordLogger.Info($"Offset 偏移已更新: X={x:F2}, Y={y:F2}");
            }
        }

        /// <summary>
        /// 更新 Offset 完整偏移（XYZ）
        /// </summary>
        public void OffsetFull(double x, double y, double z)
        {
            lock (_lockObj)
            {
                GetOffset().SetOffset(x, y, z);
                RefreshAllCoordinateMatrices();
            }
        }

        #endregion
    }
}