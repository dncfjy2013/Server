using Entity.StateManager;
using System;
using System.Threading.Tasks;
using Test.CommonTest;

namespace Test.Entity
{
    public class StateMachine
    {
        public static async void Test()
        {
            // 1. 初始化光刻机状态机管理器（设备ID：Litho-001，可替换为实际设备ID）
            var stateManager = new LithoStateMachineManager("Litho-001");

            try
            {
                // 2. 设备复位（从终止状态恢复至就绪状态）
                Console.WriteLine("=== 步骤1：设备复位 ===");
                bool resetSuccess = await stateManager.ResetMachineAsync();
                if (resetSuccess)
                {
                    Console.WriteLine("设备复位成功，当前对外状态：");
                    stateManager.TryGetExternalState(out var resetExternalState);
                    Console.WriteLine($"- 对外状态：{resetExternalState}");
                    stateManager.TryGetInternalState(out var resetInternalState);
                    Console.WriteLine($"- 对内状态：{resetInternalState}\n");
                }
                else
                {
                    Console.WriteLine("设备复位失败，终止流程\n");
                    return;
                }

                // 3. 触发生产流程（完整链路：上料→预对准→精准对准→曝光→下料）
                Console.WriteLine("=== 步骤2：启动生产流程 ===");
                // 3.1 触发晶圆上料（对内状态切换为WaferLoading）
                bool loadSuccess = await stateManager.TriggerWaferLoadingAsync();
                if (loadSuccess)
                {
                    Console.WriteLine("晶圆上料成功");
                    stateManager.TryGetInternalState(out var loadingState);
                    Console.WriteLine($"当前对内状态：{loadingState}\n");
                }
                else
                {
                    Console.WriteLine("晶圆上料失败，上报异常");
                    await stateManager.ReportErrorAsync(LithoInternalState.LoadingError, "晶圆上料失败，卡料异常");
                    return;
                }

                // 3.2 预对准（对内状态切换为WaferPreAligning）
                bool preAlignSuccess = await stateManager.TriggerPreAlignAsync();
                if (preAlignSuccess)
                {
                    Console.WriteLine("预对准完成");
                    stateManager.TryGetInternalState(out var preAlignState);
                    Console.WriteLine($"当前对内状态：{preAlignState}\n");
                }
                else
                {
                    Console.WriteLine("预对准失败，上报异常");
                    await stateManager.ReportErrorAsync(LithoInternalState.AligningError, "预对准偏差超出阈值");
                    return;
                }

                // 3.3 精准对准（对内状态切换为WaferPreciseAligning）
                bool preciseAlignSuccess = await stateManager.TriggerPreciseAlignAsync();
                if (preciseAlignSuccess)
                {
                    Console.WriteLine("精准对准完成，准备曝光");
                    stateManager.TryGetInternalState(out var preciseAlignState);
                    Console.WriteLine($"当前对内状态：{preciseAlignState}\n");
                }
                else
                {
                    Console.WriteLine("精准对准失败，上报异常");
                    await stateManager.ReportErrorAsync(LithoInternalState.AligningError, "精准对准偏差过大");
                    return;
                }

                // 3.4 曝光操作（核心工艺，对内状态切换为Exposing）
                bool exposeSuccess = await stateManager.TriggerExposingAsync();
                if (exposeSuccess)
                {
                    Console.WriteLine("曝光完成");
                    stateManager.TryGetInternalState(out var exposeState);
                    Console.WriteLine($"当前对内状态：{exposeState}\n");
                }
                else
                {
                    Console.WriteLine("曝光失败，上报异常");
                    await stateManager.ReportErrorAsync(LithoInternalState.ExposureError, "光强异常，曝光失败");
                    return;
                }

                // 3.5 晶圆下料（对内状态切换为WaferUnloading）
                bool unloadSuccess = await stateManager.TriggerWaferUnloadingAsync();
                if (unloadSuccess)
                {
                    Console.WriteLine("晶圆下料完成，单批生产结束");
                    stateManager.TryGetInternalState(out var unloadState);
                    Console.WriteLine($"当前对内状态：{unloadState}");
                    stateManager.TryGetExternalState(out var unloadExternalState);
                    Console.WriteLine($"当前对外状态：{unloadExternalState}\n");
                }
                else
                {
                    Console.WriteLine("晶圆下料失败，上报异常");
                    await stateManager.ReportErrorAsync(LithoInternalState.UnloadingError, "晶圆下料卡料");
                    return;
                }

                // 4. 生产完成，切换至空闲状态
                Console.WriteLine("=== 步骤3：生产完成，切换至空闲状态 ===");
                await stateManager.TransitionToIdleAsync();
                stateManager.TryGetExternalState(out var finalExternalState);
                stateManager.TryGetInternalState(out var finalInternalState);
                Console.WriteLine($"最终对外状态：{finalExternalState}");
                Console.WriteLine($"最终对内状态：{finalInternalState}");

                // 5. 查看状态变更日志
                Console.WriteLine("\n=== 状态变更日志（最近10条） ===");
                var logs = stateManager.GetStateChangeLogs();
                foreach (var log in logs)
                {
                    Console.WriteLine($"{log.Timestamp:yyyy-MM-dd HH:mm:ss} | 设备ID：{log.MachineId} | 从{log.FromInternalState}→{log.ToInternalState} | 对外状态：{log.CurrentExternalState}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n流程异常：{ex.Message}");
                // 异常时触发紧急终止，同步更新对外状态
                await stateManager.ManualTerminateAsync();
                stateManager.TryGetExternalState(out var errorExternalState);
                Console.WriteLine($"紧急终止后对外状态：{errorExternalState}");
            }
            finally
            {
                // 释放资源
                stateManager.Dispose();
                Console.WriteLine("\n资源释放完成，流程结束");
            }
        }
    }

    // 补充状态机管理类的扩展方法（对接原有管理类，无需修改原有代码）
    public static class LithoStateMachineExtensions
    {
        // 触发晶圆上料
        public static async Task<bool> TriggerWaferLoadingAsync(this LithoStateMachineManager manager)
        {
            return await manager.TriggerInternalStateAsync(LithoInternalState.WaferLoading, "设备控制模块触发晶圆上料");
        }

        // 触发预对准
        public static async Task<bool> TriggerPreAlignAsync(this LithoStateMachineManager manager)
        {
            return await manager.TriggerInternalStateAsync(LithoInternalState.WaferPreAligning, "设备控制模块触发预对准");
        }

        // 触发精准对准
        public static async Task<bool> TriggerPreciseAlignAsync(this LithoStateMachineManager manager)
        {
            return await manager.TriggerInternalStateAsync(LithoInternalState.WaferPreciseAligning, "设备控制模块触发精准对准");
        }

        // 触发曝光
        public static async Task<bool> TriggerExposingAsync(this LithoStateMachineManager manager)
        {
            return await manager.TriggerInternalStateAsync(LithoInternalState.Exposing, "设备控制模块触发曝光");
        }

        // 触发晶圆下料
        public static async Task<bool> TriggerWaferUnloadingAsync(this LithoStateMachineManager manager)
        {
            return await manager.TriggerInternalStateAsync(LithoInternalState.WaferUnloading, "设备控制模块触发晶圆下料");
        }

        // 切换至空闲状态
        public static async Task TransitionToIdleAsync(this LithoStateMachineManager manager)
        {
            await manager.TriggerInternalStateAsync(LithoInternalState.TaskCompleted, "生产完成，切换至空闲状态");
        }

        // 触发紧急终止
        public static async Task ManualTerminateAsync(this LithoStateMachineManager manager)
        {
            await manager.TriggerInternalStateAsync(LithoInternalState.EmergencyTerminate, "手动触发紧急终止");
        }

        // 通用触发对内状态切换
        private static async Task<bool> TriggerInternalStateAsync(this LithoStateMachineManager manager, LithoInternalState targetState, string reason)
        {
            return await manager._internalStateMachine.TransitionAsync(manager._machineId, targetState, reason);
        }
    }
}
