# 客户方案字段模板

本文档用于新增客户方案时参考。实际导入文件必须保存为标准 JSON，放在 `CH_V03.Plugins/profiles/*.json`，不能保留 `//` 注释。

```jsonc
{
  // 客户方案唯一编号，建议格式：客户拼音_产品_工站，例如 SuzhouWeiXin_18165_ICT。
  "Id": "Customer_Product_TestType",

  // 导入窗口显示名称，建议包含客户、产品、工站。
  "Name": "客户名称 产品型号 工站",

  // 客户名称，用于方案列表排序和窗口标题。
  "CustomerName": "客户名称",

  // 产品名称或产品型号。
  "ProductName": "产品型号",

  // 下位机协议适配器编号。默认 ICT 协议用 Default.Ict；客户协议不同则新增适配器。
  "ProtocolAdapterId": "Default.Ict",

  // 方案备注，说明来源项目、MES 类型、导出格式、协议差异等。
  "Notes": "方案说明",

  // 导入后写入主程序的完整配置。
  "Configuration": {
    // 基础身份信息。
    "Identity": {
      // 客户名称。
      "CustomerName": "客户名称",

      // 产品名称或产品型号。
      "ProductName": "产品型号",

      // 测试类型，例如 ICT、FCT、AOI。
      "TestType": "ICT",

      // 测试程序版本或软件版本，例如 18165-ICT-V1.0。
      "TestProgram": "Program-Version",

      // 工站编号，没有可留空。
      "StationNo": "",

      // 机台编号、设备 IP 或设备编号。
      "MachineNo": "",

      // 条码长度规则，没有固定长度可留空。
      "BarcodeLength": "",

      // 条码后缀规则，没有可留空。
      "CodeSuffix": ""
    },

    // 通讯配置，包含扫码枪和下位机。
    "Communication": {
      // 是否需要主动触发扫码枪扫码。
      "ScannerTriggerEnabled": false,

      // 扫码枪触发命令，常见值为 start\r\n、TON 或 TON\r\n。
      "ScannerTriggerCommand": "start\\r\\n",

      // 扫码枪停止命令。部分扫码枪需要在读码后发送 TOFF 或 TOFF\r\n；不需要时留空。
      "ScannerStopCommand": "",

      // 扫码枪连接配置。
      "Scanner": {
        // 设备显示名称。
        "Name": "扫码枪",

        // 连接类型：0=串口，1=TCP 客户端，2=TCP 服务端，3=禁用。
        "ConnectionType": 0,

        // 串口号，串口模式时使用。
        "PortName": "COM9",

        // 串口波特率，串口模式时使用。
        "BaudRate": 9600,

        // IP 地址，TCP 模式时使用。
        "IpAddress": "127.0.0.1",

        // 端口号，TCP 模式时使用。
        "Port": 0,

        // 通讯超时时间，单位毫秒。
        "TimeoutMilliseconds": 3000
      },

      // 下位机连接配置。
      "LowerMachine": {
        // 设备显示名称。
        "Name": "下位机",

        // 连接类型：0=串口，1=TCP 客户端，2=TCP 服务端，3=禁用。
        "ConnectionType": 0,

        // 串口号，串口模式时使用。
        "PortName": "COM11",

        // 串口波特率，串口模式时使用。
        "BaudRate": 115200,

        // IP 地址，TCP 模式时使用。
        "IpAddress": "127.0.0.1",

        // 端口号，TCP 模式时使用。
        "Port": 0,

        // 通讯超时时间，单位毫秒。
        "TimeoutMilliseconds": 3000
      }
    },

    // MES 配置。
    "Mes": {
      // 是否启用 MES。
      "Enabled": false,

      // 是否在线模式。客户方案导入时建议先为 false，现场确认后再开启。
      "OnlineMode": false,

      // 是否启用良率管控上传。
      "EnableYieldControlUploadToMes": false,

      // 良率阈值百分比。
      "YieldControlThreshold": 90,

      // 当前选择的 MES 插件编号，例如 SwiftMom.Http、SuzhouWeiXin.Http。
      "SelectedPlugin": "None",

      // 通用 MES 基础地址；部分插件不用此字段，会在 PluginOptions 中配置完整 URL。
      "BaseUrl": "",

      // MES 用户账号。
      "UserNo": "",

      // MES 用户密码。
      "Password": "",

      // MES 工站编号。
      "StationNo": "",

      // MES 包装号、批次号或客户定义字段。
      "PackNo": "",

      // 治具号、工具号或 fixture 编号。
      "Tooling": "",

      // MES 产品编号。
      "ProdNo": "",

      // MES 异常时是否允许离线缓存。
      "AllowOfflineCache": true,

      // MES 插件专用参数。键名由插件定义，例如 PreCheckUrl、UploadUrl。
      "PluginOptions": {
        // 预校验接口地址。
        "PreCheckUrl": "",

        // 测试结果上传接口地址。
        "UploadUrl": "",

        // 接口超时时间，单位秒。
        "TimeoutSeconds": "3",

        // MES 测试类型。
        "TestType": "ICT",

        // MES 前一站测试类型，没有可填 NULL 或留空，按客户接口要求。
        "PrevTestType": "NULL",

        // MES 产品名称。
        "ProductName": "产品型号",

        // MES 作业区域。
        "WorkArea": "",

        // MES 厂区或站点。
        "Site": "",

        // MES 程序名或产品程序编号。
        "Program": "",

        // MES 批次号，没有可填 NULL 或留空，按客户接口要求。
        "Lot": "NULL",

        // 上传给 MES 的设备 IP。
        "IpAddress": "",

        // 上传给 MES 的工具号或治具号。
        "ToolNumber": "",

        // 槽位。
        "SlotName": "1",

        // 子槽位。
        "SubSlotName": "A",

        // 资源名或机台名。
        "ResourceName": "",

        // 物料批次，多个值按客户插件约定分隔。
        "MaterialLotNames": "",

        // 软件版本或程序版本。
        "SwVersion": ""
      }
    },

    // 基础输出路径配置。
    "Output": {
      // 主 CSV 数据输出目录。
      "CsvDirectory": "D:\\VisionResult\\FPC\\ProData\\",

      // DCL 或客户指定结果输出目录。
      "DclDirectory": "D:\\VisionResult\\FPC\\ProOut\\",

      // 日志输出目录。
      "LogDirectory": "D:\\VisionResult\\FPC\\Log\\",

      // 默认 CSV 名称或产品名。
      "CsvName": "ProductName"
    },

    // 导出插件配置。
    "Export": {
      // 启用的导出插件编号列表，可同时启用多个。
      "EnabledPluginIds": [
        "Default.Csv"
      ],

      // 各导出插件专用参数，第一层键为插件编号。
      "PluginOptions": {
        "Default.Csv": {
          // 插件输出目录。
          "CsvDirectory": "D:\\VisionResult\\FPC\\ProData\\",

          // 产品名，用于文件名或内容字段。
          "ProName": "ProductName",

          // 治具号。
          "FixtureNo": "",

          // 设备 IP。
          "IpAddress": "",

          // 测试类型。
          "TestType": "ICT",

          // 测试员或测试设备编号。
          "TesterId": "",

          // 配置名或客户自定义配置字段。
          "Config": "",

          // Holder 字段。
          "Holder": "",

          // Slot 字段。
          "Slot": "",

          // 软件版本。
          "SwVersion": ""
        }
      }
    },

    // 兼容旧项目的扩展配置。
    "Compatibility": {
      // 当前客户方案编号，通常与顶层 Id 一致。
      "ProjectProfileId": "Customer_Product_TestType",

      // 下位机协议适配器编号，通常与顶层 ProtocolAdapterId 一致。
      "ProtocolAdapterId": "Default.Ict",

      // 第三方 DLL 目录，例如 CH_V03.Plugins/libs 下的客户目录。
      "ThirdPartyLibraryDirectory": ""
    },

    // 弹窗配置。
    "Popup": {
      // 是否启用测试结果弹窗或提示。
      "Enabled": true,

      // 弹窗是否要求人工确认。
      "RequireConfirmation": false
    },

    // 测试项目配置。
    "TestItems": {
      // 测试项目列表。实际项目应按客户旧项目或下位机返回顺序维护。
      "Items": [
        {
          // 项目顺序，从 1 开始。
          "Order": 1,

          // 项目名称。
          "ItemName": "J1_1-SHORT",

          // 上限。
          "UpperLimit": "10",

          // 下限。
          "LowerLimit": "0",

          // 单位。
          "Unit": "ohm",

          // 测点 A。
          "PointA": "1",

          // 测点 B。
          "PointB": "1"
        }
      ]
    },

    // 用户和运行习惯配置。
    "User": {
      // 是否自动扫码。
      "AutoScan": true,

      // 连续 NG 报警次数。
      "ConsecutiveNgAlarmCount": 5,

      // 默认登录工号。
      "LoginJobNo": "admin",

      // 当前治具号。
      "FixtureNo": "",

      // 用户账号列表。导入客户方案时程序会保留当前系统账号，方案里可留空。
      "Accounts": [
        {
          // 登录用户名。
          "UserName": "operator",

          // 显示名称。
          "DisplayName": "操作员",

          // 登录密码。
          "Password": "123456",

          // 角色：0=操作员，1=技术员，2=工程师，3=管理员。
          "Role": 0
        }
      ]
    }
  }
}
```

## 新增客户方案建议

1. 复制现有 `CH_V03.Plugins/profiles/*.json` 作为新文件。
2. 先填写 `Id`、`Name`、`CustomerName`、`ProductName`、`ProtocolAdapterId`。
3. 再填写通讯、MES、导出路径和插件参数。
4. 如果旧项目的下位机命令、OK/NG 回复、测试数据格式不同，先新增协议适配器，再把 `ProtocolAdapterId` 指向新适配器。
5. 如果旧项目 MES 或导出格式不同，新增独立插件，不要把客户逻辑写进主程序。
