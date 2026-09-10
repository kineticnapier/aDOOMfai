# aDOOMfai

**A Dance of Fire and Ice の中で DOOM を動かすプロジェクト。**

当初は PACL2 だけで DOOM 風レンダラを作る実験として始まり、数千個の `MoveDecorations` 呼び出し、フレームバッファのバッチ更新を経て、最終的にネイティブの `doomgeneric` を ADOFAI 内へ組み込む構成になりました。

現在の最終版では、DOOM 互換エンジンをネイティブ C で動かし、C# 製の ADOFAI / Unity Mod から接続し、**320x200** のフレームバッファを 1 個の ADOFAI Decoration に表示します。

## 構成

```text
ADOFAI 譜面
    |
    | PACL2 で一度だけ attach
    v
ADOOMFAIAccelerator.dll (C# / JALib / UMM)
    |
    | P/Invoke
    v
adoom_native.dll (C)
    |
    v
doomgeneric / DOOM engine
    |
    | 320x200 RGBA framebuffer
    v
Unity Texture2D
    |
    v
ADOFAI Decoration
```

最初の attach が終わった後、PACL2 は DOOM の毎フレーム描画ループには関与しません。

## 現在の状態

- ネイティブ doomgeneric エンジン: 動作確認済み
- 320x200 フレームバッファ: 動作確認済み
- IWAD 読み込み: 動作確認済み
- キーボード入力: 動作確認済み
- ADOFAI 内への表示: 動作確認済み
- Point Filter での表示: 対応済み
- 上下反転問題: 修正済み
- ネイティブ音声 / 音楽: この PoC では未実装

## 必要なもの

- Windows
- A Dance of Fire and Ice
- Unity Mod Manager / JALib 環境
- PACL2
- Mod をビルドできる .NET SDK
- GCC / CMake / Ninja を入れた MSYS2 UCRT64
- 対応する IWAD

ビルドスクリプトは通常、次の MSYS2 パスを自動検出します。

```text
C:\msys64\ucrt64\bin
```

## ビルド

```powershell
.\build.ps1 -ManagedPath "C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice_Data\Managed"
```

ビルド時には以下を行います。

1. 固定した revision の `doomgeneric` を取得
2. `adoom_native.dll` をビルド
3. `ADOOMFAIAccelerator.dll` をビルド
4. Mod をパッケージ化

## IWAD

**商用 DOOM の IWAD はこのリポジトリには含まれていません。**

対応する IWAD を次の場所へ 1 個入れてください。

```text
Mods\ADOOMFAIAccelerator\iwad\
```

例:

```text
DOOM.WAD
DOOM2.WAD
freedoom1.wad
freedoom2.wad
```

`ADOOMFAI_IWAD` 環境変数で WAD のパスを直接指定することもできます。

本家 DOOM の WAD を使う場合は、自分が正規に所有しているものを使用してください。自由に配布可能な代替データとして Freedoom も使用できます。

## 操作

| キー | 動作 |
|---|---|
| ↑ / W | 前進 |
| ↓ / S | 後退 |
| ← / A | 左を向く |
| → / D | 右を向く |
| Ctrl | 攻撃 |
| Space | 使用 / ドアを開ける |
| Shift | 走る |
| 1-7 | 武器変更 |
| Tab | オートマップ |
| Esc | メニュー |
| Enter | 決定 |

現状の `A/D` はストレイフではなく旋回です。

## 譜面

最小構成のラッパー譜面は [`chart/`](chart/) にあります。

内容は、320x200 のフレームバッファ表示用 Decoration 1 個と、ネイティブ DOOM ホストを attach する PACL2 Program だけです。

## このプロジェクトの経緯

2048 個の Decoration を直接更新する方式から、バッチ描画、320x200 化、そして doomgeneric の組み込みへ至るまでの経緯は [`docs/CHALLENGE.md`](docs/CHALLENGE.md) にまとめています。

最終的な構成は、厳密には「ADOFAI だけで DOOM を再実装した」のではなく、**ネイティブの DOOM 互換エンジンを ADOFAI 内でホストし、その映像と入力を ADOFAI に接続したもの**です。

## ライセンス

ADOOMFAI のソースコードは、リンクしている DOOM 由来コードとの互換性のため **GPL-2.0-or-later** で配布します。詳細は [`LICENSE`](LICENSE) を参照してください。

商用 DOOM のゲームデータはこのソースコードのライセンス対象ではなく、このリポジトリでは配布しません。

## 注意

このプロジェクトは非公式の技術実験 / ファンプロジェクトです。id Software、Bethesda、ZeniMax、7th Beat Games、doomgeneric の開発者とは関係ありません。
