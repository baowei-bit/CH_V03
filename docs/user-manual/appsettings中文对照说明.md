# `appsettings.json` 中文对照说明

适用文件：`src/CH_V03.App/config/appsettings.json`。该文件保存工站的基础资料、通信、MES、导出、账号和测试项目配置。

> 修改前请先退出软件并备份原文件。JSON 的字段名、引号、逗号和层级必须保留；布尔值使用 `true` / `false`，不要加引号。程序运行后保存配置时会按当前结构重新写入文件。

## 一、根级字段

| 字段 | 当前值 | 中文含义与说明 |
| --- | --- | --- |
| `WindowTitle` | `CeHao - 默认产品` | 历史兼容字段。当前窗口标题由 `Identity.CustomerName` 和 `Identity.ProductName` 自动组合，手工修改本字段不会成为最终标题。 |
| `DefaultOperatorLogin` | `false` | `true` 时启动跳过登录窗口，直接使用 `AutoLoginUserName` 指定的账号登录。建议现场正常使用时保持 `false`。 |
| `AutoLoginUserName` | `huaqin` | 自动登录使用的账号。仅在 `DefaultOperatorLogin=true` 时生效；账号必须存在于 `User.Accounts` 中。 |

## 二、`Identity`：产品与条码规则

| 字段 | 当前值 | 中文含义 / 填写方式 |
| --- | --- | --- |
| `CustomerName` | 空 | 客户名称；与产品名一起构成窗口标题。 |
| `ProductName` | `默认产品` | 产品名称。 |
| `TestType` | `ICT` | 测试类型，例如 `ICT`、`FCT`。可被导出器文件名模板引用。 |
| `TestProgram` | `FCT-V1.0` | 测试程序/软件版本标识，部分 MES 或导出插件会使用。 |
| `StationNo` | `111` | 工站号。 |
| `MachineNo` | 空 | 设备号/机器号；仅在客户方案或插件需要时填写。 |
| `BarcodeLength` | `18` | 条码长度规则。使用条码校验时填写期望长度；不需要时按现场规则保留。 |
| `CodeSuffix` | 空 | 条码必须包含的末尾字符串；留空表示不校验后缀。 |
| `ScanRuleEnabled` | `false` | 是否启用条码长度、前缀和后缀等基础校验。 |
| `ScanRulePrefix` | 空 | 启用基础条码校验后要求的条码前缀；留空不校验前缀。 |
| `AllowContinuousRepeatTest` | 未配置，默认 `false` | 是否允许同一条码连续重复开始测试。`false` 时仅拦截与上一笔实际开始测试相同的条码。该字段可省略，程序会使用默认值。 |
| `BarcodePositionValidationEnabled` | `false` | 是否启用指定位置字符校验。 |
| `BarcodeValidationPosition` | `8` | 要校验的位置，按业务规则使用的从 1 开始的位置填写。 |
| `BarcodeValidationAllowedValues` | `L,B` | 该位置允许的字符，使用英文逗号分隔，例如 `L,B`。 |
| `BarcodeLimitRuleEnabled` | `false` | 是否按条码指定字符动态覆盖某个测试项目的上下限。 |
| `BarcodeLimitItemIndex` | `12` | 要被动态改限的测试项序号。 |
| `BarcodeLimitUpperByValue` | `L=630;B=630` | 指定字符到上限的映射；用分号分隔，格式为 `字符=上限`。 |
| `BarcodeLimitLowerByValue` | `L=550;B=480` | 指定字符到下限的映射；用分号分隔，格式为 `字符=下限`。 |

## 三、`Communication`：扫码枪与下位机

### 通用控制

| 字段 | 当前值 | 中文含义 / 填写方式 |
| --- | --- | --- |
| `AutoConnectScannerOnStartup` | `true` | 启动时自动连接扫码枪。 |
| `AutoConnectLowerMachineOnStartup` | `true` | 启动时自动连接下位机。 |
| `ScannerTriggerEnabled` | `false` | 是否启用下位机触发扫码枪的流程。 |
| `ScannerTriggerCommand` | `start\\r\\n` | 触发扫码命令。`\\r\\n` 表示回车换行，应按扫码枪协议填写。 |
| `ScannerStopCommand` | 空 | 停止扫码命令；无此协议时留空。 |
| `ScannerWaitTimeoutSeconds` | `10` | 触发扫码后等待条码返回的最长秒数。 |
| `TestItemRequestEnabled` | `false` | 是否向下位机请求测试项目/项目数量。 |
| `TestItemRequestCommand` | `Itemnum\\r\\n` | 请求测试项目的下位机命令。 |
| `LowerMachinePayloadTimeoutSeconds` | `60` | 下位机一条完整报文从开始接收到结束的最长秒数。 |
| `BarcodeSendDefaultOkCommand` | `false` | 是否按条码指定字符自动发送放行命令。 |
| `BarcodeSuccessCommandByValue` | `L=DMOK1\\r\\n;B=DMOK2\\r\\n` | 条码字符到放行命令的映射；格式为 `字符=命令;字符=命令`。 |

### `Scanner` / `LowerMachine`：设备连接对象

两者字段完全相同；当前扫码枪使用 USB POS HID（`ConnectionType=4`），下位机使用串口（`ConnectionType=0`）。

| 字段 | 中文含义 | `ConnectionType=0` 串口时 | `ConnectionType=1/2` TCP 时 |
| --- | --- | --- | --- |
| `Name` | 界面显示名称 | 填写设备名称 | 填写设备名称 |
| `ConnectionType` | 连接方式 | `0` 串口 | `1` TCP 客户端、`2` TCP 服务端、`3` 禁用、`4` USB POS HID |
| `PortName` | 串口名 | 如 `COM9`、`COM11` | 不使用 |
| `BaudRate` | 串口波特率 | 如 `9600`、`115200` | 不使用 |
| `IpAddress` | 网络地址 | 不使用 | 例如 `192.168.1.10`；服务端通常监听本机地址 |
| `Port` | TCP 端口 | 不使用 | 例如 `5000` |
| `TimeoutMilliseconds` | 连接/读写超时毫秒数 | 建议按设备协议填写 | 建议按网络情况填写 |

## 四、`Mes`：MES 总开关与通用连接信息

| 字段 | 当前值 | 中文含义 / 注意事项 |
| --- | --- | --- |
| `Enabled` | `true` | 是否启用 MES 功能。 |
| `OnlineMode` | `false` | 是否以在线模式执行 MES 调用；上线前应先完成插件配置和连通性验证。 |
| `EnableYieldControlUploadToMes` | `false` | 是否按良率阈值控制 MES 上传。 |
| `YieldControlThreshold` | `90` | 良率控制阈值（百分比）。 |
| `SelectedPlugin` | `JiaLianYi.CareerRule` | 当前选择的 MES 插件 ID，必须与插件的内部 ID 一致。`HongXin OA` 的 ID 为 `HongXin.OA`。 |
| `BaseUrl` | `http://127.0.0.1:8080/swiftmom/api/` | 通用 MES 地址；实际使用哪个地址取决于所选插件。 |
| `UserNo` / `Password` | 空 / 空 | MES 账号和密码；如协议不需要可留空。不要在对外发送的配置文件中保留真实密码。 |
| `StationNo` | 空 | MES 站位；为空时部分插件会回退使用 `Identity.StationNo`。 |
| `PackNo` | 空 | 箱号/包装号，按客户协议使用。 |
| `Tooling` | 空 | 治具号。 |
| `ProdNo` | `456` | 工单号/生产号。 |
| `AllowOfflineCache` | `true` | MES 不可用时是否允许离线缓存，恢复后再处理；需结合现场追溯规则使用。 |
| `PluginOptions` | 见下 | 当前 MES 插件的附加字段，键和值均为字符串。当前 `Unit`、`Module`、`JobNo`、`Station` 分别是单位、模块、工单、站位。 |

## 五、`Output`：默认文件输出路径

| 字段 | 当前值 | 中文含义 |
| --- | --- | --- |
| `CsvDirectory` | `D:\\VisionResult\\FPC\\ProData\\` | 默认产品 CSV 保存目录。 |
| `ClassifyCsvByResult` | `false` | `true` 时在 CSV 目录下按 `PASS`、`FAIL` 子目录分类保存。 |
| `DclDirectory` | `D:\\VisionResult\\FPC\\ProOut\\` | 默认 DCL 输出目录。 |
| `LogDirectory` | `D:\\VisionResult\\FPC\\Log\\` | 运行日志目录。 |
| `CustomerPathSaveEnabled` | `false` | 是否启用客户指定目录规则。 |
| `CustomerPathRootDirectory` | `D:\\VisionResult\\FPC\\CORR\\` | 客户路径规则的根目录。 |
| `CsvName` | `F3409-0021-0021CS` | 默认 CSV 文件前缀。 |
| `SinglePieceCsvEnabled` | `false` | 是否输出单片 CSV。 |
| `SinglePieceCsvDirectory` | `D:\\VisionResult\\FPC\\SingleCsv\\` | 单片 CSV 保存目录。 |
| `SinglePieceCsvFileName` | `{Result}_{SN}_{Time}.csv` | 单片 CSV 名称模板；可用 `{Result}`、`{SN}`、`{Time}`。 |

## 六、`Export`：导出器启用与专属选项

### 导出器总配置

| 字段 | 当前值 | 中文含义 |
| --- | --- | --- |
| `EnabledPluginIds` | `["Custom.Csv"]` | 已启用的导出器 ID 列表；当前仅启用自定义 CSV。需要多导出器时，在数组中增加相应 ID。 |
| `PluginOptions` | 多个对象 | 按导出器 ID 保存的附加参数；未启用的导出器参数可以保留，不会自动执行。 |

### `JiaLianYi.Export`：嘉联益 CSV/DCL 导出器

当前已配置：`Unit`（单位）、`Module`（模块）、`JobNo`（工单）、`Station`（站位）。其余常用字段如下。

| 字段 | 中文含义 |
| --- | --- |
| `CsvDirectory` / `FailCsvDirectory` / `DclDirectory` | CSV、FAIL CSV、DCL 的专用目录。 |
| `CsvName` | CSV 文件前缀。 |
| `PartNumber` / `ItemCode` | 产品料号 / 品号。 |
| `ModifyUser` / `Mechine` / `Tooling` | 测试人员 / 设备 / 治具。 |
| `OperationSeq` / `SwVersion` | 制程序号 / 软件版本。 |
| `DclEnabled` | 是否生成 DCL，使用字符串 `"true"` 或 `"false"`。 |
| `LegacyDirectoryLayout` / `UseLegacyDclFormat` | 是否兼容旧版目录结构 / DCL 格式。 |
| `DclExtension` | DCL 扩展名，例如 `.txt`。 |

### `YiDongEap.RawCsv`：奕东 EAP 测试结果导出器

| 字段 | 当前值 | 中文含义 / 填写方式 |
| --- | --- | --- |
| `OutputFormat` | `BOTH` | 输出格式：`CSV`、`HYTM_TXT` 或 `BOTH`。 |
| `ExportHorizontalCsv` / `ExportVerticalCsv` | `true` / `true` | 是否分别导出横版、竖版 CSV；使用字符串 `"true"` / `"false"`。 |
| `HorizontalCsvDirectory` / `CsvDirectory` | `...\\h` / `...\\s` | 横版 / 竖版 CSV 保存目录。 |
| `HorizontalCsvName` | `YiDongEap` | 横版 CSV 文件前缀。 |
| `BoardId` | `B1` | 板号（CSV 中的 `BX` / `BoardID`）。 |
| `SectionName` | `CompTest` | CSV 测试段名称。 |
| `FileSuffix` | `RIGHT` | CSV 文件名尾缀。 |
| `HytmTitle` | `HYTM1_1_1 Statistic` | HYTM TXT 标题。 |
| `BlockNum` / `BlockGroup` | `1` / `0-0` | HYTM Block 编号 / 分组。 |
| `OsErrorCode` / `CircuitErrorCode` | `0` / `0` | HYTM OSErr / CERR。 |

可按需要增加但当前未配置的字段：`HytmTxtDirectory`（HYTM 目录，留空使用竖版 CSV 目录）、`ProgramId`、`PartNumber`、`Station`、`DeviceId`、`FixtureId`、`LineId`、`OpenTestIndexes`、`ShortTestIndexes`、`CompTestIndexes`、`FunTestIndexes`、`FunTestContent`、`Operator`、`TestFilePath`、`EquipmentNo`。

**竖版 CSV 列说明：** `+%` 输出测试项目 `UpperLimit`（上限），`-%` 输出 `LowerLimit`（下限）；此规则同时适用于 CompTest 和 FunTest。

### `Custom.Csv`：自定义 CSV 导出器

| 字段组 | 当前值 | 中文含义 |
| --- | --- | --- |
| `FileNameTemplate` | `{ProductName}_{TestType}_{SN}_{Result}_{Time}` | 文件名模板。可用 `{ProductName}`、`{TestType}`、`{SN}`、`{Result}`、`{Time}`。 |
| `HeaderSN`、`HeaderResult`、`HeaderFailedItems`、`HeaderTestTime`、`HeaderStation` | 均为 `true` | 是否输出 SN、结果、不良项、测试时间、站位等摘要表头。 |
| `BodyIndex`、`BodyItemName`、`BodyPointA`、`BodyPointB` | 均为 `true` | 是否输出序号、项目名、点位 A、点位 B。 |
| `BodyLowerLimit`、`BodyUpperLimit`、`BodyUnit`、`BodyTestValue`、`BodyResult` | 均为 `true` | 是否输出下限、上限、单位、测试值、测试结果。 |

## 七、`Compatibility`：兼容性与客户方案

| 字段 | 当前值 | 中文含义 |
| --- | --- | --- |
| `EnableKunshanJiaLianYiDedicatedConfiguration` | `false` | 是否启用昆山嘉联益专用精简配置界面；启用后会锁定对应 MES/导出插件。 |
| `ProjectProfileId` | 空 | 已导入客户方案的 ID。 |
| `ProtocolAdapterId` | `Default.Ict` | 下位机协议适配器 ID；默认使用 ICT 协议。 |
| `ThirdPartyLibraryDirectory` | `libs` | 客户第三方 DLL 的相对目录。 |
| `VisibleConfigurationFields` | `[]` | 可显示的配置字段白名单；空数组表示不限制显示。 |

## 八、`Popup`：运行消息提醒

| 字段 | 当前值 | 中文含义 |
| --- | --- | --- |
| `Enabled` | `true` | 是否显示运行消息提醒。 |
| `RequireConfirmation` | `false` | 是否要求操作员确认后关闭提醒。 |
| `CenterAndEnlarge` | `false` | 是否在主界面中央放大显示提醒。 |
| `RedAlert` | `false` | 是否使用红色警示配色。 |

## 九、`TestItems.Items`：测试项目与限值

每个数组元素代表一个测试项目。当前示例包含 `J1_1-SHORT`、`R1`、`U1-ID` 三项。

| 字段 | 中文含义 / 示例 |
| --- | --- |
| `Order` | 项目序号，建议从 1 连续递增；奕东导出器的测试项分类也按该序号填写。 |
| `ItemName` | 测试项目名称，如 `R1`。 |
| `UpperLimit` / `LowerLimit` | 上限 / 下限；可填写数值或协议支持的文本，例如 `0x38`。 |
| `Unit` | 测量单位，例如 `ohm`、`Kohm`、`HEX`。 |
| `PointA` / `PointB` | 测试点位 A / B。 |

## 十、`User`：操作方式、良率与账号

### 一般用户设置

| 字段 | 当前值 | 中文含义 |
| --- | --- | --- |
| `AutoScan` | `false` | 是否自动扫码。 |
| `ManualScanMode` | `true` | 是否使用手动扫码模式。 |
| `NoScanMode` | `false` | 是否允许无扫码测试。 |
| `ScanAheadEnabled` | `true` | 手动扫码时先完成条码校验/MES 卡关，收到下位机 DM 后再放行测试。 |
| `SaveDailyQuantity` | `false` | 是否在重启后保留当天 PASS/FAIL 数量。 |
| `DailyYieldStatistics` | 日期空、PASS/FAIL 为 0 | 保存的当天统计；仅在 `SaveDailyQuantity=true` 时使用。 |
| `ConsecutiveNgAlarmCount` | `5` | 连续 NG 报警阈值。 |
| `LoginJobNo` | `admin` | 当前操作员工号/登录工号，可供插件回退使用。 |
| `FixtureNo` | 空 | 治具号。 |

### `User.Jly`：昆山嘉联益专用规则

| 字段 | 当前值 | 中文含义 |
| --- | --- | --- |
| `RunMode` | `2` | 嘉联益运行模式：`0` 标准片、`1` 生产、`2` 调试。调试模式的 CSV 文件名会追加 `_debug`。 |
| `TestPieceMode` | `false` | 兼容旧版字段；`true` 等同于生产模式。新配置以 `RunMode` 为准。 |
| `DebugMode` | `true` | 派生字段，由 `RunMode=2` 得出；无需手工依赖此字段。 |
| `YieldUploadThreshold` | `90` | 生产模式下良率小于或等于此值时不上传 MES。 |
| `UseSnTimeForTxtFileName` | `false` | 是否按 SN 加时间命名 TXT 文件。 |
| `ShowFailResultPopup` | `false` | 是否在失败时显示结果弹窗。 |
| `RequireNumericTestValues` | `false` | 是否要求测试值为数值。 |
| `RecordSecondTestYield` | `false` | 是否将第二次测试计入良率。 |

### `User.Accounts`：账号列表

| 子字段 | 中文含义 |
| --- | --- |
| `UserName` | 登录账号，区分大小写不敏感。 |
| `DisplayName` | 界面显示名称。 |
| `Password` | 登录密码；当前为明文保存，配置文件必须限制访问权限。 |
| `Role` | 权限角色：`0` 操作员、`1` 技术员、`2` 工程师、`3` 管理员。 |

当前默认账号：`operator / 123456`（操作员）、`engineer / 123`（工程师）、`admin / admin`（管理员）、`huaqin / admin`（华勤管理员）。程序启动时会自动创建或修复这些默认账号；若需要自定义密码，请通过管理员账号在软件内修改，并限制配置文件的访问权限。

## 十一、修改建议

1. 优先在软件的配置界面修改并保存；直接编辑 JSON 仅适用于批量部署或软件无法启动时的修复。
2. 修改通信、MES、导出路径后，先在现场进行连接测试和单件验证，再开启自动连接或在线模式。
3. 路径中的反斜杠在 JSON 中必须写成 `\\`，例如 `D:\\Result\\Csv\\`。
4. 切换客户方案时，应同时检查 `Identity`、`Mes.SelectedPlugin`、`Export.EnabledPluginIds`、`Compatibility` 和测试项目，避免混用不同客户的数据格式。
