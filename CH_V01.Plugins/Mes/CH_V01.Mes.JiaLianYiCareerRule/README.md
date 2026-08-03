# 嘉联益 CareerRule MES 插件

插件 ID：`JiaLianYi.CareerRule`

该插件兼容旧项目 `嘉联益(昆山)\0033\WinForm_PY_Ict` 中的 MES 调用方式，内部封装 `CareerRule.dll`：

- 测试连接：`Rule.checkAPILink()`
- 测试前卡关：`Rule.checkFuntionTestMachineWork(...)`
- 测试结果上传：`Rule.insertFuntionTestData(...)`

## 配置字段

建议在 `Mes.PluginOptions` 中配置：

```json
{
  "PartNumber": "Y5006-0036-0020B22",
  "Station": "T09_Flag",
  "ModifyUser": "21041310",
  "Mechine": "Z00000001",
  "Tooling": "FK00061837-0001",
  "JobNo": "261A06Y23",
  "OperationSeq": "9500",
  "ItemCode": "Y5006-0036-0020B22",
  "Unit": "pcs",
  "Module": "1",
  "SwVersion": "FCT-V0522"
}
```

运行时 DLL 放置：

```text
plugins\mes\CH_V01.Mes.JiaLianYiCareerRule.dll
plugins\mes\CareerRule.dll
```
