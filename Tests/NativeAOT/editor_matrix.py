import unreal

cls = unreal.load_class(None, '/Script/UnrealSharp.NativeAotMatrix_C')
assert cls is not None
matrix = unreal.new_object(cls)
mask = matrix.call_method('RunMatrix')
assert mask == 1023, mask
assert matrix.call_method('CheckPublishedContent') == 42
assert matrix.call_method('StaticAdd', args=(19, 23)) == 42
derived = unreal.new_object(unreal.load_class(None, '/Script/UnrealSharp.NativeAotDerived_C'))
assert derived.call_method('Add', args=(13, 29)) == 42
tick_probe = unreal.new_object(unreal.load_class(None, '/Script/UnrealSharp.NativeAotTickProbe_C'))
tick_probe.call_method('ReceiveTick', args=(1.25,))
assert tick_probe.get_editor_property('TickValue') == 125
unreal.log('NATIVEAOT_EDITOR_MATRIX PASS mask=1023 static=42 inherited=42 override=125')

map_path = '/Game/NativeAotValidation/Validation'
if not unreal.EditorAssetLibrary.does_asset_exist(map_path):
    world = unreal.EditorLoadingAndSavingUtils.new_blank_map(False)
    assert unreal.EditorLoadingAndSavingUtils.save_map(world, map_path)
unreal.log('NATIVEAOT_VALIDATION_MAP ' + map_path)
