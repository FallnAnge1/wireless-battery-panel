# 读取协议与证据边界

## 通用标准 HID（2.13.0）

- Generic Device Controls Page `0x06` / Battery Strength `0x20`：按描述符逻辑最小/最大值归一化为百分比。
- Battery System Page `0x85` / Relative State of Charge `0x64`：仅接受描述符声明的 0–100 百分比范围。不把 Remaining Capacity 等绝对容量字段当百分比。
- 由 `HidP_GetValueCaps` 确认字段，使用 `HidD_GetFeature` 或 `HidD_GetInputReport` 只读取得报告，再由 `HidP_GetUsageValue` 提取，支持有效的 0% 值。
- 仅读取明确声明的 Report ID，单接口最多四种报告；不发送私有命令，不进行连续输入监听。部分设备不支持控制式 GET_REPORT，即使有字段也可能无法读取。
- 多字段或同物理设备多个接口读数冲突时不猜测，不把失败缓存成满电。慢查询每接口最多一个、全局最多四个，后台返回后再采纳；桌面界面不等待无限请求。
- 已验证结构布局、范围换算、字段筛选、冲突抑制、查询限流与错误恢复；本机当前没有连接可成功上报标准字段的其他设备，不宣称新增型号已实测。
- [HIDP_VALUE_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_value_caps)、[HidP_GetUsageValue](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getusagevalue)。

## HI98 Pro

- 接收器：`VID_3554 / PID_FA09`，厂商集合 `FF02 / 0002`，输入/输出 20 字节。
- 电量查询：`13 4A 01 00 ... 5E`，只发送现有电量读取命令。
- 校验：固定头、长度、0–100 范围以及前 19 字节求和校验；百分比位于字节 5。
- 字节 6 在本机一次插拔对照中由 `01` 变为 `10`，据此显示充电提示。
- 插线后的百分比会立即上报为 100%，不把它映射为充满。协议含义未获厂商确认，不能承诺校准精度。

## G3M PRO

- 接收器 `320F:706E`，有线 `320F:706B`；集合 `FF1C / 0092`，64 字节报文。
- 只读电量请求头：`04 20 00 1A 06`，其余补 0。
- 回复需匹配报文头、命令、长度、状态、电量范围与校验约定；百分比位于字节 8，状态字节 9。
- 状态 `02` 为充电；状态 `01` 仅在有线模式识别为充电，百分比为 100 时按此协议标为已充满。未知组合不猜测。
- 本机观察到回复保留请求校验值 `0020`。不得把其他型号任意回复解释为本协议。

## Windows 标准属性

- 电量：Windows 公开电量属性及 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`。
- 充电：`System.Devices.ChargingState`。`0` 为未充电、`1` 为充电、`2` 为未知。
- 子节点仅按有效且一致的容器关联；显式未充电、主节点已有电量或设备未连接时，不用猜测值覆盖。
- 空容器、全 F 容器及本机出现的异常共享容器值不用于关联。USB 优先使用实际物理根设备路径。

硬件电量、USB 接收器存在和蓝牙连接状态是不同证据。软件不通过修改灯光、按键、DPI、休眠设置或固件来检测电量。
