# PBHMes

鹏博辉（深圳）原 `WinForm_PY_Ict` 程序的 MES 兼容插件。

调用顺序为：`clientLogin`（取得 `token`）→ `checkRoute`（测试前卡关）→ `createRoute`（上传测试结果）。

结果上传的 `testItem` 是 JSON 字符串，字段与原程序一致：`Sort`、`Item`、`USL`、`LSL`、`Unit`、`Value`、`Raust`。其中 `Raust` 是原接口使用的历史字段名，不能改为 `Result`。
