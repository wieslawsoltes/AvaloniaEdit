import { dotnet } from './_framework/dotnet.js';
try {
  const runtime = await dotnet.withApplicationArgumentsFromQuery().create();
  const config = runtime.getConfig();
  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
  await runtime.runMain(config.mainAssemblyName, [location.href]);
  if (new URLSearchParams(location.search).get('test') === '1') {
    const bridge = exports.Scribe.Browser.TestBridge;
    globalThis.scribe = {
      ready: true,
      state: () => JSON.parse(bridge.State()),
      execute: (command, argument = '') => bridge.Execute(command, argument),
      bounds: name => JSON.parse(name === 'completion' ? bridge.CompletionBounds() : bridge.Bounds(name)),
      point: offset => JSON.parse(bridge.Point(offset)),
      probe: command => bridge.Probe(command)
    };
  }
  document.getElementById('loading')?.remove();
} catch (error) {
  console.error(error);
  globalThis.scribeError = String(error.stack || error);
  const target = document.getElementById('error');
  if (target) target.textContent = globalThis.scribeError;
}
