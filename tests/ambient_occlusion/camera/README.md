# Authored AO camera regression

The parent ambient-occlusion CMake suite includes this directory with `add_subdirectory(camera)`. It derives the real `black_ao_camera_publish` helper and `black_pc_ram_span` guard from `BLACK_MANUAL_SOURCE` into the build directory. Extraction also requires exactly one original projection-producer call and one publisher call, with temporary FOV-window restoration between them. No generated guest function, game asset, save or private path is needed.

The C harness supplies bounded synthetic primary/contiguous RAM and an observing renderer setter. Each plain/trace run executes 18,567 checks covering actual memory guards, valid boundary cameras, UI/secondary-camera suppression, invalid float encodings, all rounding and MXCSR status combinations, FTZ/DAZ, and precision unmasked with pending MXCSR precision. The setter deliberately disturbs the masked host FP environment; the returned producer fenv/MXCSR must remain exact. Trace-enabled tests include the host logging path.

These tests validate the native authored hook and its guest controls. Guest x87 is emulated in doubles/SSE. A true native x87 pending/unmasked trap state is outside this suite's contract. GPU depth/frame association and visual output have separate renderer/runtime checks.
