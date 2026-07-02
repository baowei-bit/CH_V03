# 嘉联益导出插件

插件 ID：`JiaLianYi.Export`

该插件用于兼容 `嘉联益(昆山)\0033\WinForm_PY_Ict` 的导出格式：

- CSV：首行主表头，后续 Lower Limit / Upper Limit / Measurement Unit / high_point / low_point 五行辅助表头，再追加测试数据行。
- DCL：以条码命名的 `.txt` 文件，包含点位、规格、测试值和最终 OK/NG。

启用方式：在配置中加入：

```json
"Export": {
  "EnabledPluginIds": [ "JiaLianYi.Export" ]
}
```

可选参数放在：

```json
"Export": {
  "PluginOptions": {
    "JiaLianYi.Export": {
      "CsvDirectory": "D:\\VisionResult\\FPC\\ProData\\",
      "DclDirectory": "D:\\VisionResult\\FPC\\ProOut\\",
      "CsvName": "F3409-0021-0021CS",
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
  }
}
```
