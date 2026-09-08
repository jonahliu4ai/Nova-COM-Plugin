#!/bin/bash
# ============================================================
# NovaCOMPlugin 一键发布脚本（V4.5.1 起）
#
# 功能：
#   1. 编译 NovaCOMPluginV4.5.dll（零依赖，无需 /reference）
#   2. 打包 dist/NovaCOMPlugin-vX.Y.Z.zip（含 DLL + devices/ 配置目录）
#   3. 可选：上传到 GitHub Release（需 GH_TOKEN 环境变量）
#
# 用法：
#   ./create-plugin-release.sh            # 仅本地打包
#   ./create-plugin-release.sh --upload   # 打包 + 上传 GitHub Release
#
# 注意：devices/ 目录必须与 DLL 一起分发，否则 JSON 配置驱动无法工作
# ============================================================

set -e
cd "$(dirname "$0")"

VERSION="4.5.1"
DLL_SRC="NovaCOMPluginV4.5.cs"
DLL_OUT="NovaCOMPluginV4.5.dll"
DEVICES_DIR="devices"
DIST_DIR="dist"
PKG_NAME="NovaCOMPlugin-v${VERSION}.zip"
CSC="/c/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe"

REPO="jonahliu4ai/Nova-COM-Plugin"
TAG="plugin-v${VERSION}"

# ---------- 1. 编译 ----------
echo "[1/4] 编译 $DLL_OUT ..."
"$CSC" /target:library /out:$DLL_OUT $DLL_SRC
echo "      OK"

# ---------- 2. 检查依赖文件 ----------
echo "[2/4] 检查发布内容 ..."
missing=0
for f in "$DLL_OUT" "$DEVICES_DIR"; do
    if [ ! -e "$f" ]; then
        echo "      [缺失] $f"
        missing=1
    fi
done
if [ "$missing" = "1" ]; then
    echo "[错误] 缺少必要文件，终止"
    exit 1
fi
json_count=$(ls "$DEVICES_DIR"/*.json 2>/dev/null | wc -l)
echo "      DLL: $DLL_OUT"
echo "      设备配置: $json_count 个 JSON ($DEVICES_DIR/)"

# ---------- 3. 本地打包 ----------
echo "[3/4] 打包 $DIST_DIR/$PKG_NAME ..."
mkdir -p "$DIST_DIR"
rm -f "$DIST_DIR/$PKG_NAME"

# zip 需要相对路径：在临时 staging 目录中组织
STAGING="$DIST_DIR/staging"
rm -rf "$STAGING"
mkdir -p "$STAGING/NovaCOMPlugin-v$VERSION"
cp "$DLL_OUT" "$STAGING/NovaCOMPlugin-v$VERSION/"
cp -r "$DEVICES_DIR" "$STAGING/NovaCOMPlugin-v$VERSION/"

PS="/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
(cd "$STAGING/NovaCOMPlugin-v$VERSION" && \
    "$PS" -NoProfile -Command "Compress-Archive -Path * -DestinationPath '..\\$PKG_NAME' -Force")
mv "$STAGING/$PKG_NAME" "$DIST_DIR/$PKG_NAME"
rm -rf "$STAGING"

PKG_SIZE=$(du -h "$DIST_DIR/$PKG_NAME" | cut -f1)
echo "      OK ($PKG_SIZE)"

# ---------- 4. 可选上传 ----------
if [ "$1" != "--upload" ]; then
    echo ""
    echo "[完成] 本地发布包: $DIST_DIR/$PKG_NAME"
    echo "       上传请运行: ./create-plugin-release.sh --upload"
    exit 0
fi

if [ -z "$GH_TOKEN" ]; then
    echo ""
    echo "[错误] 上传需要 GH_TOKEN 环境变量"
    echo "       设置: export GH_TOKEN=ghp_xxxxxxxx"
    echo "       或仅本地打包: ./create-plugin-release.sh"
    exit 1
fi

echo "[4/4] 上传 GitHub Release ($TAG) ..."
BODY="NovaCOMPlugin V${VERSION} — JSON 配置驱动的电化学外围设备控制插件（零外部依赖）\n\n## 内容\n- $DLL_OUT（单文件 DLL，无 System.Web.Extensions 依赖）\n- devices/ 目录：$json_count 个设备 JSON 配置\n\n## 安装\n将 DLL 与 devices/ 目录一起拷贝到 NOVA 2.1 插件目录即可"

CREATE_RESP=$(curl -s -X POST \
    -H "Authorization: token $GH_TOKEN" \
    -H "Accept: application/vnd.github.v3+json" \
    https://api.github.com/repos/$REPO/releases \
    -d "{
        \"tag_name\": \"$TAG\",
        \"target_commitish\": \"main\",
        \"name\": \"NovaCOMPlugin v${VERSION}\",
        \"body\": \"$BODY\",
        \"draft\": false,
        \"prerelease\": false
    }")

UPLOAD_URL=$(echo "$CREATE_RESP" | grep -o '"upload_url":"[^"]*' | cut -d'"' -f4 | sed 's/{?name,label}//')
RELEASE_ID=$(echo "$CREATE_RESP" | grep -o '"id":[0-9]*' | head -1 | cut -d':' -f2)

if [ -z "$UPLOAD_URL" ]; then
    echo "[错误] 创建 release 失败"
    echo "响应: $CREATE_RESP"
    exit 1
fi

echo "      Release 创建成功 (ID: $RELEASE_ID)，上传 $PKG_NAME ..."
curl -s -X POST \
    -H "Authorization: token $GH_TOKEN" \
    -H "Content-Type: application/zip" \
    "$UPLOAD_URL?name=$PKG_NAME" \
    --data-binary "@$DIST_DIR/$PKG_NAME" > /dev/null

echo ""
echo "[完成] Release 已发布！"
echo "链接: https://github.com/$REPO/releases/tag/$TAG"
