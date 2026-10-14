import { dotnet } from './_framework/dotnet.js';
try {
  const runtime = await dotnet.withApplicationArgumentsFromQuery().create();
  const config = runtime.getConfig();
  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
  await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
  if (new URLSearchParams(location.search).get('test') === '1') {
    const bridge = exports.ProText.Browser.TestBridge;
    globalThis.protext = {
      state: () => JSON.parse(bridge.GetState()),
      execute: (command, argument = '') => bridge.Execute(command, argument),
      bounds: name => JSON.parse(bridge.GetBounds(name)),
      ready: true
    };
  }
  document.getElementById('loading')?.remove();
} catch (error) {
  console.error(error);
  const target = document.getElementById('error');
  if (target) target.textContent = String(error.stack || error);
  globalThis.protextError = String(error.stack || error);
}
