using Server.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.StateManager
{
    /// <summary>
    /// 光刻机对外状态（MES系统、上位机、用户界面可见）
    /// 简洁标准化，仅暴露设备核心运行状态，不展示内部细节
    /// </summary>
    public enum LithoExternalState
    {
        /// <summary>
        /// 终止状态（设备停机、流程终止，需手动复位）
        /// </summary>
        Terminate,

        /// <summary>
        /// 生产状态（设备正在执行晶圆曝光全流程）
        /// </summary>
        Producing,

        /// <summary>
        /// 空闲状态（设备就绪，等待生产任务/配方）
        /// </summary>
        Idle,

        /// <summary>
        /// 异常状态（设备故障，无法正常运行，需排查处理）
        /// </summary>
        Error,
    }

    /// <summary>
    /// 光刻机对内状态（设备内部控制、工程师调试可见）
    /// 精细化到动作级，用于设备流程管控、故障定位、动作触发
    /// 与对外状态形成一对多映射，统一收敛为对外状态
    /// </summary>
    public enum LithoInternalState
    {
        // 对应对外状态：Terminate（终止状态）
        /// <summary>
        /// 设备断电（完全停机，未上电）
        /// </summary>
        PowerOff,
        /// <summary>
        /// 手动终止（操作员主动停止设备所有流程）
        /// </summary>
        ManualTerminate,
        /// <summary>
        /// 紧急终止（急停触发，设备立即停止所有动作）
        /// </summary>
        EmergencyTerminate,

        // 对应对外状态：Producing（生产状态）
        /// <summary>
        /// 晶圆上料（从晶圆盒取片，送至预对准工位）
        /// </summary>
        WaferLoading,
        /// <summary>
        /// 预对准（粗定位晶圆，确保后续精准对准）
        /// </summary>
        WaferPreAligning,
        /// <summary>
        /// 精准对准（Stage平台移动，实现晶圆与掩膜版精准对位）
        /// </summary>
        WaferPreciseAligning,
        /// <summary>
        /// 曝光准备（调整光强、焦距，确认配方参数）
        /// </summary>
        ExposurePreparing,
        /// <summary>
        /// 曝光中（核心工艺，执行晶圆曝光动作）
        /// </summary>
        Exposing,
        /// <summary>
        /// 晶圆下料（曝光完成，将晶圆送回晶圆盒）
        /// </summary>
        WaferUnloading,

        // 对应对外状态：Idle（空闲状态）
        /// <summary>
        /// 系统就绪（设备自检完成，等待生产任务）
        /// </summary>
        SystemReady,
        /// <summary>
        /// 等待配方（已就绪，等待MES下发生产配方）
        /// </summary>
        WaitingForRecipe,
        /// <summary>
        /// 任务完成（单批晶圆生产完成，等待下一批任务）
        /// </summary>
        TaskCompleted,

        // 对应对外状态：Error（异常状态）
        /// <summary>
        /// 上料异常（晶圆取片失败、卡料）
        /// </summary>
        LoadingError,
        /// <summary>
        /// 对准异常（对位偏差超出阈值，无法正常曝光）
        /// </summary>
        AligningError,
        /// <summary>
        /// 曝光异常（光强异常、焦距偏差，曝光失败）
        /// </summary>
        ExposureError,
        /// <summary>
        /// 系统故障（设备核心部件故障，如Stage、光源故障）
        /// </summary>
        SystemError,
        /// <summary>
        /// 下料异常（晶圆送回失败、卡料）
        /// </summary>
        UnloadingError
    }

    /// <summary>
    /// 光刻机状态机管理类
    /// 统一管控：对内状态机、对外状态机、状态映射、流转规则、日志审计
    /// 提供标准化接口，适配MES、设备控制模块、上位机调用
    /// </summary>
    public class LithoStateMachineManager : IDisposable
    {
        #region 核心字段
        /// <summary>
        /// 设备ID（唯一标识，支持多设备管理）
        /// </summary>
        public readonly string _machineId;

        /// <summary>
        /// 对内状态机（设备内部控制，精细化动作管理）
        /// </summary>
        public readonly StateMachine<string, LithoInternalState> _internalStateMachine;

        /// <summary>
        /// 对外状态机（MES/上位机可见，标准化状态展示）
        /// </summary>
        public readonly StateMachine<string, LithoExternalState> _externalStateMachine;

        /// <summary>
        /// 状态映射锁（保证多线程下映射一致性）
        /// </summary>
        private readonly object _stateMapLock = new object();

        /// <summary>
        /// 状态变更日志（用于追溯、调试）
        /// </summary>
        private readonly ConcurrentQueue<StateChangeLog> _stateChangeLogs = new ConcurrentQueue<StateChangeLog>();
        #endregion

        #region 构造函数（初始化状态机、流转规则、映射关系）
        public LithoStateMachineManager(string machineId)
        {
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId), "设备ID不能为空");

            // 初始化内外状态机
            _internalStateMachine = new StateMachine<string, LithoInternalState>();
            _externalStateMachine = new StateMachine<string, LithoExternalState>();

            // 初始化内外状态机的初始状态
            _internalStateMachine.InitializeState(_machineId, LithoInternalState.PowerOff);
            _externalStateMachine.InitializeState(_machineId, LithoExternalState.Terminate);

            // 配置对内状态流转规则（贴合光刻机实际运行流程，禁止非法动作）
            ConfigureInternalTransitions();

            // 配置对外状态流转规则（标准化，适配MES交互）
            ConfigureExternalTransitions();

            // 订阅对内状态变更事件，同步更新对外状态
            _internalStateMachine.OnAfterTransition += OnInternalStateChanged;
        }
        #endregion

        #region 核心配置（流转规则）
        /// <summary>
        /// 配置对内状态流转规则（精细化控制，禁止非法动作）
        /// 遵循光刻机实际运行流程：上电→就绪→生产→完成→空闲/异常
        /// </summary>
        private void ConfigureInternalTransitions()
        {
            // 终止相关流转（Terminate映射）
            _internalStateMachine.AddTransition(LithoInternalState.PowerOff, LithoInternalState.ManualTerminate);
            _internalStateMachine.AddTransition(LithoInternalState.PowerOff, LithoInternalState.EmergencyTerminate);
            _internalStateMachine.AddTransition(LithoInternalState.ManualTerminate, LithoInternalState.PowerOff);
            _internalStateMachine.AddTransition(LithoInternalState.EmergencyTerminate, LithoInternalState.PowerOff);
            // 所有状态均可紧急终止
            foreach (var state in Enum.GetValues<LithoInternalState>())
            {
                if (state != LithoInternalState.EmergencyTerminate)
                {
                    _internalStateMachine.AddTransition(state, LithoInternalState.EmergencyTerminate);
                }
            }

            // 生产相关流转（Producing映射）
            _internalStateMachine.AddTransition(LithoInternalState.SystemReady, LithoInternalState.WaferLoading);
            _internalStateMachine.AddTransition(LithoInternalState.WaferLoading, LithoInternalState.WaferPreAligning);
            _internalStateMachine.AddTransition(LithoInternalState.WaferPreAligning, LithoInternalState.WaferPreciseAligning);
            _internalStateMachine.AddTransition(LithoInternalState.WaferPreciseAligning, LithoInternalState.ExposurePreparing);
            _internalStateMachine.AddTransition(LithoInternalState.ExposurePreparing, LithoInternalState.Exposing);
            _internalStateMachine.AddTransition(LithoInternalState.Exposing, LithoInternalState.WaferUnloading);
            _internalStateMachine.AddTransition(LithoInternalState.WaferUnloading, LithoInternalState.TaskCompleted);

            // 空闲相关流转（Idle映射）
            _internalStateMachine.AddTransition(LithoInternalState.TaskCompleted, LithoInternalState.WaitingForRecipe);
            _internalStateMachine.AddTransition(LithoInternalState.WaitingForRecipe, LithoInternalState.SystemReady);
            _internalStateMachine.AddTransition(LithoInternalState.SystemReady, LithoInternalState.WaitingForRecipe);

            // 异常相关流转（Error映射）
            _internalStateMachine.AddTransition(LithoInternalState.WaferLoading, LithoInternalState.LoadingError);
            _internalStateMachine.AddTransition(LithoInternalState.WaferPreAligning, LithoInternalState.AligningError);
            _internalStateMachine.AddTransition(LithoInternalState.WaferPreciseAligning, LithoInternalState.AligningError);
            _internalStateMachine.AddTransition(LithoInternalState.ExposurePreparing, LithoInternalState.ExposureError);
            _internalStateMachine.AddTransition(LithoInternalState.Exposing, LithoInternalState.ExposureError);
            _internalStateMachine.AddTransition(LithoInternalState.WaferUnloading, LithoInternalState.UnloadingError);
            // 所有生产/空闲状态均可进入系统故障
            var productionAndIdleStates = new[]
            {
                LithoInternalState.SystemReady, LithoInternalState.WaitingForRecipe, LithoInternalState.TaskCompleted,
                LithoInternalState.WaferLoading, LithoInternalState.WaferPreAligning, LithoInternalState.WaferPreciseAligning,
                LithoInternalState.ExposurePreparing, LithoInternalState.Exposing, LithoInternalState.WaferUnloading
            };
            foreach (var state in productionAndIdleStates)
            {
                _internalStateMachine.AddTransition(state, LithoInternalState.SystemError);
            }
            // 异常状态可复位至断电（终止）
            foreach (var errorState in new[]
                     {
                         LithoInternalState.LoadingError, LithoInternalState.AligningError,
                         LithoInternalState.ExposureError, LithoInternalState.SystemError, LithoInternalState.UnloadingError
                     })
            {
                _internalStateMachine.AddTransition(errorState, LithoInternalState.PowerOff);
            }
        }

        /// <summary>
        /// 配置对外状态流转规则（标准化，适配MES交互）
        /// </summary>
        private void ConfigureExternalTransitions()
        {
            // 对外状态流转：终止→空闲→生产→空闲/异常；异常→终止；生产可直接终止/异常
            _externalStateMachine.AddTransition(LithoExternalState.Terminate, LithoExternalState.Idle);
            _externalStateMachine.AddTransition(LithoExternalState.Idle, LithoExternalState.Producing);
            _externalStateMachine.AddTransition(LithoExternalState.Producing, LithoExternalState.Idle);
            _externalStateMachine.AddTransition(LithoExternalState.Producing, LithoExternalState.Error);
            _externalStateMachine.AddTransition(LithoExternalState.Producing, LithoExternalState.Terminate);
            _externalStateMachine.AddTransition(LithoExternalState.Idle, LithoExternalState.Terminate);
            _externalStateMachine.AddTransition(LithoExternalState.Error, LithoExternalState.Terminate);
        }
        #endregion

        #region 状态映射（对内→对外，核心逻辑）
        /// <summary>
        /// 对内状态映射为对外状态（统一收敛，避免对外暴露内部细节）
        /// </summary>
        /// <param name="internalState">对内状态</param>
        /// <returns>对外状态</returns>
        private LithoExternalState MapToExternalState(LithoInternalState internalState)
        {
            lock (_stateMapLock)
            {
                return internalState switch
                {
                    // 对内终止类状态 → 对外Terminate
                    LithoInternalState.PowerOff or LithoInternalState.ManualTerminate or LithoInternalState.EmergencyTerminate
                        => LithoExternalState.Terminate,

                    // 对内生产类状态 → 对外Producing
                    LithoInternalState.WaferLoading or LithoInternalState.WaferPreAligning or
                    LithoInternalState.WaferPreciseAligning or LithoInternalState.ExposurePreparing or
                    LithoInternalState.Exposing or LithoInternalState.WaferUnloading
                        => LithoExternalState.Producing,

                    // 对内空闲类状态 → 对外Idle
                    LithoInternalState.SystemReady or LithoInternalState.WaitingForRecipe or LithoInternalState.TaskCompleted
                        => LithoExternalState.Idle,

                    // 对内异常类状态 → 对外Error
                    LithoInternalState.LoadingError or LithoInternalState.AligningError or
                    LithoInternalState.ExposureError or LithoInternalState.SystemError or LithoInternalState.UnloadingError
                        => LithoExternalState.Error,

                    // 默认状态（异常兜底）
                    _ => LithoExternalState.Error
                };
            }
        }

        /// <summary>
        /// 对内状态变更后，同步更新对外状态
        /// </summary>
        private async void OnInternalStateChanged(string key, LithoInternalState fromState, LithoInternalState toState)
        {
            if (key != _machineId) return;

            // 映射对外状态
            var externalState = MapToExternalState(toState);
            // 同步更新对外状态机
            await _externalStateMachine.TransitionAsync(_machineId, externalState, reason: $"内部状态从{fromState}切换至{toState}，同步更新对外状态");
            // 记录状态变更日志
            _stateChangeLogs.Enqueue(new StateChangeLog(
                DateTimeOffset.UtcNow,
                _machineId,
                fromState,
                toState,
                externalState));

            // 日志数量限制（保留最近1000条）
            while (_stateChangeLogs.Count > 1000)
            {
                _stateChangeLogs.TryDequeue(out _);
            }
        }
        #endregion

        #region 对外接口（供MES、上位机调用，仅暴露对外状态）
        /// <summary>
        /// 获取设备当前对外状态（MES/上位机调用）
        /// </summary>
        /// <returns>对外状态</returns>
        public bool TryGetExternalState(out LithoExternalState externalState)
        {
            return _externalStateMachine.TryGetCurrentState(_machineId, out externalState);
        }

        /// <summary>
        /// 手动终止设备（MES/操作员调用，触发对外Terminate状态）
        /// </summary>
        public async Task<bool> ManualTerminateAsync()
        {
            return await _internalStateMachine.TransitionAsync(
                _machineId,
                LithoInternalState.ManualTerminate,
                reason: "操作员手动终止设备");
        }

        /// <summary>
        /// 设备复位（从终止状态恢复至空闲状态，MES/操作员调用）
        /// </summary>
        public async Task<bool> ResetMachineAsync()
        {
            // 先从PowerOff切换至SystemReady（对内），自动映射为对外Idle
            return await _internalStateMachine.TransitionAsync(
                _machineId,
                LithoInternalState.SystemReady,
                reason: "设备复位，从终止状态恢复至就绪状态");
        }

        /// <summary>
        /// 获取状态变更日志（最近1000条，供追溯）
        /// </summary>
        public StateChangeLog[] GetStateChangeLogs()
        {
            return _stateChangeLogs.ToArray();
        }
        #endregion

        #region 对内接口（供设备控制模块调用，控制精细化动作）
        /// <summary>
        /// 触发晶圆上料动作（设备控制模块调用）
        /// </summary>
        public async Task<bool> TriggerWaferLoadingAsync()
        {
            return await _internalStateMachine.TransitionAsync(
                _machineId,
                LithoInternalState.WaferLoading,
                reason: "设备控制模块触发晶圆上料",
                transitionAction: async (key, from, to) =>
                {
                    // 此处可接入实际设备上料逻辑（如控制机械臂、传感器检测）
                    await Task.Delay(100); // 模拟设备动作耗时
                });
        }

        /// <summary>
        /// 触发曝光动作（设备控制模块调用）
        /// </summary>
        public async Task<bool> TriggerExposingAsync()
        {
            return await _internalStateMachine.TransitionAsync(
                _machineId,
                LithoInternalState.Exposing,
                reason: "设备控制模块触发曝光动作",
                transitionAction: async (key, from, to) =>
                {
                    // 此处可接入实际曝光逻辑（如控制光源、Stage平台扫描）
                    await Task.Delay(500); // 模拟曝光耗时（核心工艺，耗时较长）
                });
        }

        /// <summary>
        /// 触发异常上报（设备控制模块检测到故障时调用）
        /// </summary>
        public async Task<bool> ReportErrorAsync(LithoInternalState errorState, string errorMsg)
        {
            if (!IsErrorState(errorState))
            {
                throw new ArgumentException($"状态{errorState}不是合法的异常状态", nameof(errorState));
            }

            return await _internalStateMachine.TransitionAsync(
                _machineId,
                errorState,
                reason: $"设备异常：{errorMsg}");
        }

        /// <summary>
        /// 获取当前对内状态（工程师/调试用）
        /// </summary>
        public bool TryGetInternalState(out LithoInternalState internalState)
        {
            return _internalStateMachine.TryGetCurrentState(_machineId, out internalState);
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 判断状态是否为异常状态
        /// </summary>
        private bool IsErrorState(LithoInternalState state)
        {
            return state is LithoInternalState.LoadingError or LithoInternalState.AligningError or
                   LithoInternalState.ExposureError or LithoInternalState.SystemError or LithoInternalState.UnloadingError;
        }
        #endregion

        #region 资源释放
        public void Dispose()
        {
            _internalStateMachine.Dispose();
            _externalStateMachine.Dispose();
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 状态变更日志实体（用于追溯）
        /// <summary>
        /// 状态变更日志（记录内外状态变更，用于调试、追溯）
        /// </summary>
        public sealed record StateChangeLog(
            DateTimeOffset Timestamp,
            string MachineId,
            LithoInternalState FromInternalState,
            LithoInternalState ToInternalState,
            LithoExternalState CurrentExternalState);
        #endregion
    }
}
