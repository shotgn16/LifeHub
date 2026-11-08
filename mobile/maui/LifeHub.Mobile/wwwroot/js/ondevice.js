// The on-device AI for the LifeHub phone app: WebLLM (https://github.com/mlc-ai/web-llm, Apache-2.0) on the
// phone's GPU. The model downloads once and stays in the app's storage; after that it works offline.
(function () {
  let engine = null, loaded = null, lib = null;

  async function webllm() {
    if (!lib) lib = await import('../vendor/web-llm/web-llm.js');
    return lib;
  }

  async function ensure(model, onProgress) {
    if (!navigator.gpu) throw new Error('This phone\'s web view has no WebGPU, so the on-device AI can\'t run here. Choose another AI in Settings → AI.');
    const w = await webllm();
    const progress = (r) => onProgress && onProgress(`Loading ${model}: ${r.text || ''}`);
    const appConfig = { ...w.prebuiltAppConfig, cacheBackend: 'indexeddb', useIndexedDBCache: true };
    if (!engine) engine = await w.CreateMLCEngine(model, { initProgressCallback: progress, appConfig });
    else if (loaded !== model) { engine.setInitProgressCallback(progress); await engine.reload(model); }
    loaded = model;
    return engine;
  }

  window.lifehubOnDevice = {
    supported: async () => {
      try { return !!(navigator.gpu && await navigator.gpu.requestAdapter()); } catch { return false; }
    },
    load: async (model, dotnet) => {
      await ensure(model, (t) => dotnet && dotnet.invokeMethodAsync('Report', t));
      if (dotnet) await dotnet.invokeMethodAsync('Report', `${model} is ready.`);
    },
    chat: async (model, system, user, json) => {
      const e = await ensure(model);
      const req = {
        messages: [{ role: 'system', content: system }, { role: 'user', content: user }],
        temperature: 0.2, stream: false
      };
      if (json) req.response_format = { type: 'json_object' };
      if (/qwen3/i.test(model)) req.extra_body = { enable_thinking: false };
      const r = await e.chat.completions.create(req);
      return (r.choices?.[0]?.message?.content || '').replace(/<think>[\s\S]*?<\/think>/g, '').trim();
    }
  };
})();
