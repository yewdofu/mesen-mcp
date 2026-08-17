# mesen-mcp

MesenCEの外部デバッグAPIをstdio MCPツールとして公開するサーバーです。

## 必要環境

- .NET 10 SDK
- 外部デバッグAPIに対応したMesenCE

## ビルド

```powershell
dotnet build .\MesenCE.McpServer.csproj --configuration Release
```

## 使用方法

1. MesenCEを外部デバッグAPI付きで起動します。

   ```powershell
   Mesen.exe --debugApi
   ```

2. SNES ROMをMesenCEでロードします。
3. OpenCodeの`opencode.json`へMCPサーバーを登録します。`--project`のパスはクローン先に合わせて変更してください。

   ```json
   {
     "$schema": "https://opencode.ai/config.json",
     "mcp": {
       "mesen": {
         "type": "local",
         "command": [
           "dotnet",
           "run",
           "--project",
           "C:\\path\\to\\mesen-mcp\\MesenCE.McpServer.csproj",
           "--configuration",
           "Release",
           "--no-build"
         ],
         "enabled": true
       }
     }
   }
   ```

4. OpenCodeを再起動します。以後、MCPサーバーはOpenCodeによって自動起動されます。

MesenCEへの接続は最初のツール呼び出し時に行います。MesenCEとの接続が切れた場合は、次のツール呼び出し時に再接続します。Named Pipeは単一クライアント用なので、このサーバーの実行中に別の外部デバッグAPIクライアントを同時接続することはできません。

レジスタ、現在命令、メモリの読み書きにはエミュレーションの停止が必要です。先に`mesen_pause`を呼び出してください。メモリツールはMCPから扱いやすいよう、デバッグAPIのBase64ではなく区切りなし16進文字列を入出力します。

`mesen_set_controller`は指定したボタンだけを押した状態にし、その状態を次の設定または解除まで維持します。操作後は`mesen_clear_controller`で解除してください。MCPサーバーがMesenCEから切断された場合は、全コントローラーの入力が自動的に解除されます。
