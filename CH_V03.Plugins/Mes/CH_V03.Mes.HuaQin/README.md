# HongXin MES 插件

按《江西 ICT 与 FCT 上传 OA 接口协议》上传测试结果到江西弘信 OA。

- `UploadType=ICT`：调用 `SNICTUpload`；按测试项目顺序映射到 `resistance1`、`deviation1`、`noise1`……`resistance10`，最多 14 项。
- `UploadType=FCT`：调用 `FunctionalTestUpload`；按测试项目顺序映射到 `PSDAT`、`DATA`、`DATA1`……`DATA9`、`YDATA`……`YDATA9`，最多 21 项。

必填配置为 `Endpoint`、`UploadType`、`MaterialNo`（协议 `elno`）和 `DeviceNo`（ICT 的 `Index` 或 FCT 的 `ID`）。`ItemId` 仅用于 ICT，`BatchNo` 仅用于 FCT。

接口未提供测试前卡关或独立健康检查，因此插件仅在测试完成后上传；“测试连接”会校验本地配置。

## MES 扩展上传

保留上述原 OA 上传后，插件还会调用 MES 接口：先固定调用流程单开工，再按插件配置中选择的“设备产能上传”或“设备单品过站”执行上传。正式环境地址按接口文档固定；产能接口使用 `application/x-www-form-urlencoded`，过站接口使用 Basic Auth + `application/json`。本次导出的 CSV 会以 Base64 附件上传。

测试项会按 FCT/ICT 测试分类表归入大类（例如 `OPEN`、`IC功能测试`）；每个大类上传一条对应的 MES 特性代号，具体测试项的名称、值、结果、上下限和单位组成 JSON 数组写入 `sp_require`。
