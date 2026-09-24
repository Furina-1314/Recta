# 静态库 + 动态 CRT(/MD)。
# 用因:pqxx 以 DLL 构建时,其 dllexport 类 zview 继承 std::string_view,
# 会把 STL 内联成员导出进导入库,与消费方静态库产生 LNK2005 多重定义。
# 静态链接 libpqxx/libpq/openssl 并保持 /MD,一次解决且免去运行期 DLL 分发。
set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE static)
