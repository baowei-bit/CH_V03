# libdmtx Windows runtime

Bundled `libdmtx-64.dll`: libdmtx 0.7.4, obtained from the PyPI pylibdmtx 0.1.10 win_amd64 wheel used in the sample verification. Python is not required by CeHao. SHA256: `B0EC02FE8C32C143E540036986E3488C8940F831CA51F885C8B225168070E5C2`.

The application targets x64. Build and publish copy the DLL to the application directory and the original BSD license to `licenses/libdmtx-LICENSE.txt`. Distribute both. The interop message layout is specific to this 0.7.4 binary; newer versions add fields and require updating and testing the binding.

`LibDmtxDecoder` tests the original grayscale image, candidate bounding boxes discovered from connected dark areas, thresholds 90/110/130, and rotated variants. The time budget limits retries. It contains no sample coordinates or expected barcode text. It is a fallback after the existing QR/Data Matrix reader and before the older enhancement pipeline.

The endoscope window's image-file button uses the same decoder without emitting production scan events. Real-time scans still require matching reads on distinct captured frames. Sample regressions can be run with `CH_V03.EndoscopeSmokeTest` using an expected value followed by local image paths.
