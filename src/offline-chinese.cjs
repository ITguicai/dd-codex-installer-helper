'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const SDK_KEY = 'client-sYWqzCYMRkUg4DqqiZcR5DGTNl2iD7zNJY0HoeDLzxR';
const LOCALE_CONFIG = '72216192';
// StableID uses _getStorageKey(sdkKey), which hashes the "k:" prefix too.
const STABLE_ID_KEY = 'statsig.stable_id.' + hash('k:' + SDK_KEY);
const ORIGIN = 'app://-';
const KEY_PREFIX = Buffer.from('_' + ORIGIN + '\0\x01', 'latin1');
function hash(text) {
  let result = 0;
  for (let i = 0; i < text.length; i++) result = ((result << 5) - result + text.charCodeAt(i)) | 0;
  return String(result >>> 0);
}
function storageKey(name) { return Buffer.concat([KEY_PREFIX, Buffer.from(name, 'latin1')]); }
function decode(value) {
  if (!Buffer.isBuffer(value) || value.length === 0) throw new Error('Invalid Chromium storage value');
  if (value[0] === 1) return value.subarray(1).toString('latin1');
  if (value[0] === 0) return value.subarray(1).toString('utf16le');
  throw new Error('Unsupported Chromium storage encoding');
}
function encode(text) {
  const ascii = [...text].every(c => c.charCodeAt(0) <= 255);
  return Buffer.concat([Buffer.from([ascii ? 1 : 0]), Buffer.from(text, ascii ? 'latin1' : 'utf16le')]);
}
function varint(number) {
  const bytes = [];
  let value = BigInt(number);
  while (value > 127n) { bytes.push(Number(value & 127n) | 128); value >>= 7n; }
  bytes.push(Number(value));
  return Buffer.from(bytes);
}
function patchPayload(data, configKind = 'layer_configs') {
  if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('Invalid feature cache');
  const values = { enable_i18n: true, locale_source: 'IDE' };
  if (!['layer_configs', 'dynamic_configs'].includes(configKind)) throw new Error('Unsupported locale lookup type');
  const configs = data[configKind] ??= {};
  if (data.response_format === 'init-v2' || data.__statsig_api_version === 2 || data.__statsig_api_version === '2') {
    if (!Array.isArray(data.values)) throw new Error('Unsupported feature cache format');
    const current = configs[LOCALE_CONFIG] ?? configs[hash(LOCALE_CONFIG)];
    const oldValue = current && data.values[current.v];
    const index = data.values.push({ ...(oldValue ?? {}), ...values }) - 1;
    configs[LOCALE_CONFIG] = { ...(current ?? {}), v: index, r: current?.r ?? 'default', s: current?.s ?? [] };
    if (configKind === 'layer_configs') configs[LOCALE_CONFIG].ep = [...new Set([...(current?.ep ?? []), ...Object.keys(values)])];
  } else {
    const current = configs[LOCALE_CONFIG] ?? configs[hash(LOCALE_CONFIG)];
    configs[LOCALE_CONFIG] = {
      ...(current ?? {}), name: LOCALE_CONFIG,
      value: { ...(current?.value ?? {}), ...values },
      rule_id: current?.rule_id ?? 'default', secondary_exposures: current?.secondary_exposures ?? [],
    };
    if (configKind === 'layer_configs') configs[LOCALE_CONFIG].explicit_parameters = [...new Set([...(current?.explicit_parameters ?? []), ...Object.keys(values)])];
  }
  return data;
}
function cacheResult(stableId, existing, configKind = 'layer_configs') {
  const result = existing ?? { source: 'Cache', receivedAt: Date.now(), stableID: stableId, fullUserHash: null };
  const data = existing ? JSON.parse(existing.data) : {
    feature_gates: {}, dynamic_configs: {}, layer_configs: {}, param_stores: {},
    has_updates: true, hash_used: 'djb2', time: 0,
  };
  result.data = JSON.stringify(patchPayload(data, configKind));
  // A local change invalidates a server checksum used for delta responses.
  const patched = JSON.parse(result.data);
  delete patched.full_checksum;
  delete patched.checksum;
  delete patched.checksumV2;
  result.data = JSON.stringify(patched);
  result.fullUserHash = null;
  return result;
}
function startupCacheNames(stableId) {
  const names = [];
  // The renderer has no userID before sign-in (including providers that do
  // not use OpenAI authentication); API-key auth supplies ua-<stableId>.
  for (const userID of ['', 'ua-' + stableId]) {
    for (const customIDs of [{ stableID: stableId }, { stableID: stableId, source_surface_stable_id: stableId }]) {
      const cids = Object.keys(customIDs).sort((left, right) => left.localeCompare(right)).map(key => key + '-' + customIDs[key]).join(',');
      names.push({ mode: userID ? 'API 密钥模式' : '未登录/无需登录模式', name: 'statsig.cached.evaluations.' + hash('uid:' + userID + '|cids:' + cids + '|k:' + SDK_KEY) });
    }
  }
  return names;
}
function localeValue(result, configKind = 'layer_configs') {
  const data = JSON.parse(result.data);
  const configs = data[configKind];
  const config = configs?.[LOCALE_CONFIG] ?? configs?.[hash(LOCALE_CONFIG)];
  return data.response_format === 'init-v2' || String(data.__statsig_api_version) === '2' ? data.values?.[config?.v] : config?.value;
}
function localeLookupKind(initialText, sharedText, importedSources = {}) {
  // Resolve the locale provider's actual imported hook. A successful read of
  // a different Statsig configuration category does not enable this UI.
  const call = initialText.match(new RegExp('([\\w$]+)\\([`"\\\']' + LOCALE_CONFIG + '[`"\\\']\\)'));
  if (!call) throw new Error('未找到客户端中文开关读取入口。');
  function functionBody(text, symbol) {
    const start = text.indexOf('function ' + symbol + '(');
    if (start < 0) return null;
    const next = text.indexOf('function ', start + 9);
    return text.slice(start, next < 0 ? start + 3000 : Math.min(next, start + 3000));
  }
  let source = initialText, symbol = call[1];
  // 26.1013 colocates the hook and SDK in app-initial. Older clients import
  // the hook from app-shared. Never infer the lookup type from other gates.
  if (functionBody(source, symbol) == null) {
    const imports = [...initialText.matchAll(/import\{([^}]+)\}from["']([^"']+)["'];/g)];
    const imported = imports.flatMap(match => match[1].split(',').map(item => ({ path: match[2], parts: item.trim().split(/\s+as\s+/) })))
      .find(item => (item.parts[1] ?? item.parts[0]) === symbol);
    if (!imported) throw new Error('无法识别客户端中文开关导入。');
    source = /^\.\/app-shared-.*\.js$/.test(imported.path) ? sharedText : importedSources[imported.path];
    if (typeof source !== 'string') throw new Error('无法识别客户端中文开关导入。');
    const exportStart = source.lastIndexOf('export{');
    const exportEnd = source.indexOf('}', exportStart);
    const exported = exportStart < 0 ? null : source.slice(exportStart + 7, exportEnd).split(',')
      .map(item => item.trim().split(/\s+as\s+/)).find(parts => (parts[1] ?? parts[0]) === imported.parts[0]);
    if (!exported) throw new Error('无法识别客户端中文开关导出。');
    symbol = exported[0];
  }
  const kinds = new Set(), visited = new Set();
  function trace(name, depth) {
    if (depth > 2 || visited.has(name)) return;
    visited.add(name);
    const body = functionBody(source, name);
    if (body == null) return;
    if (/\.getLayer\(/.test(body)) kinds.add('layer_configs');
    if (/\.getDynamicConfig\(/.test(body)) kinds.add('dynamic_configs');
    for (const match of body.matchAll(/(?<![\w$.])([\w$]+)\(/g)) trace(match[1], depth + 1);
  }
  trace(symbol, 0);
  if (kinds.size === 1) return [...kinds][0];
  throw new Error('此客户端的中文开关读取方式尚未适配，未修改缓存。');
}
function archiveInfo(archive) {
  const fd = fs.openSync(archive, 'r');
  try {
    const prefix = Buffer.alloc(16);
    fs.readSync(fd, prefix, 0, 16, 0);
    const size = prefix.readUInt32LE(12);
    if (size > 20000000) throw new Error('Unsupported Codex archive header');
    const buffer = Buffer.alloc(size);
    fs.readSync(fd, buffer, 0, size, 16);
    const header = JSON.parse(buffer.toString('utf8'));
    const files = [];
    function walk(node, prefix = '') {
      for (const [name, file] of Object.entries(node.files ?? {})) {
        const full = prefix ? prefix + '/' + name : name;
        if (file.files) walk(file, full);
        else files.push({ path: full, ...file });
      }
    }
    walk(header);
    const locale = files.find(f => /^webview\/assets\/zh-CN-[\w]+\.js$/.test(f.path));
    const initial = files.find(f => /^webview\/assets\/app-initial-.*\.js$/.test(f.path));
    const shared = files.find(f => /^webview\/assets\/app-shared-.*\.js$/.test(f.path));
    if (!locale || !initial || !shared) throw new Error('此客户端未发现内置中文资源，请更新官方 Codex 后重试。');
    function read(file) {
      const buffer = Buffer.alloc(file.size);
      fs.readSync(fd, buffer, 0, buffer.length, 8 + prefix.readUInt32LE(4) + Number(file.offset));
      return buffer.toString('utf8');
    }
    const initialText = read(initial);
    const sharedText = read(shared);
    if (initialText.includes('enable_i18n') && initialText.includes(LOCALE_CONFIG) && (sharedText.includes(SDK_KEY) || initialText.includes(SDK_KEY))) {
      return { locale: locale.path, localeHash: locale.integrity?.hash, configKind: localeLookupKind(initialText, sharedText), providerAsset: initial.path, layout: 'app-initial' };
    }
    // 26.1021 separates the language Provider and SDK key from app-initial.
    // Follow its actual imports; a matching setting in an unrelated asset is
    // not enough to accept a new format or change a customer's database.
    const hosts = files.filter(f => /^webview\/assets\/initialize-app-host-services-[\w]+\.js$/.test(f.path));
    if (hosts.length === 1) {
      const hostText = read(hosts[0]);
      const imports = [...hostText.matchAll(/import\{([^}]+)\}from["']([^"']+)["'];/g)];
      const initialImport = imports.find(match => match[2] === './' + path.posix.basename(initial.path));
      const keyImports = imports.filter(match => /^\.\/statsig-client-key-[\w]+\.js$/.test(match[2]));
      const keyFile = keyImports.length === 1 ? files.find(f => f.path === 'webview/assets/' + keyImports[0][2].slice(2)) : null;
      if (initialImport && keyFile && read(keyFile).includes(SDK_KEY) && hostText.includes('enable_i18n') && hostText.includes(LOCALE_CONFIG) && hostText.includes('localeOverride') && hostText.includes('initialMessages')) {
        const configKind = localeLookupKind(hostText, sharedText, { [initialImport[2]]: initialText });
        return { locale: locale.path, localeHash: locale.integrity?.hash, configKind, providerAsset: hosts[0].path, layout: 'split-host-services' };
      }
    }
    throw new Error('此 Codex 版本的语言配置格式尚未适配，未修改缓存。');
  } finally { fs.closeSync(fd); }
}
function profilePaths({ appRoot, roamingRoot, userDataRoot }) {
  const modern = fs.existsSync(path.join(appRoot, 'resources', 'owl-electron-app.json'));
  const userData = userDataRoot ?? (modern ? path.join(roamingRoot, 'Codex', 'web', 'Codex') : path.join(roamingRoot, 'Codex'));
  return { userData, directory: path.join(modern ? path.join(userData, 'Default') : userData, 'Local Storage', 'leveldb') };
}
async function enableOffline({ moduleRoot, appRoot, roamingRoot, userDataRoot }) {
  const resources = archiveInfo(path.join(appRoot, 'resources', 'app.asar'));
  const { ClassicLevel } = require(path.join(moduleRoot, 'classic-level'));
  const { userData, directory } = profilePaths({ appRoot, roamingRoot, userDataRoot });
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-') + '-' + crypto.randomBytes(3).toString('hex');
  let backup = null;
  const hadDatabase = fs.existsSync(path.join(directory, 'CURRENT'));
  fs.mkdirSync(directory, { recursive: true });
  const db = new ClassicLevel(directory, { keyEncoding: 'buffer', valueEncoding: 'buffer' });
  const stateFile = path.join(userData, 'statsig-state.json');
  let state = {};
  if (fs.existsSync(stateFile)) state = JSON.parse(fs.readFileSync(stateFile, 'utf8').replace(/^\uFEFF/, ''));
  if (!state || typeof state !== 'object' || Array.isArray(state)) throw new Error('Invalid Codex identity state');
  const stateId = state['statsig-stable-id'];
  let changed = 0;
  try {
    // Keep LevelDB's exclusive OS lock through backup and the atomic batch.
    await db.open();
    if (hadDatabase) {
      backup = directory + '.dd-chinese-backup-' + timestamp;
      fs.cpSync(directory, backup, { recursive: true, errorOnExist: true, filter: file => path.basename(file) !== 'LOCK' });
    }
    let savedId;
    try {
      const rawId = await db.get(storageKey(STABLE_ID_KEY));
      if (rawId != null) savedId = JSON.parse(decode(rawId));
    }
    catch (e) { if (e.code !== 'LEVEL_NOT_FOUND') throw e; }
    const stableId = typeof stateId === 'string' && stateId.length > 0 ? stateId : typeof savedId === 'string' && savedId.length > 0 ? savedId : crypto.randomUUID();
    const rows = await db.iterator().all();
    const ops = [];
    const caches = new Map();
    for (const [key, value] of rows) {
      if (!key.subarray(0, KEY_PREFIX.length).equals(KEY_PREFIX)) continue;
      const name = key.subarray(KEY_PREFIX.length).toString('latin1');
      if (!name.startsWith('statsig.cached.evaluations.')) continue;
      const existing = JSON.parse(decode(value));
      if (typeof existing.data !== 'string') throw new Error('Invalid cached feature data');
      const patched = cacheResult(stableId, existing, resources.configKind);
      caches.set(name, patched);
      ops.push({ type: 'put', key, value: encode(JSON.stringify(patched)) });
      changed++;
    }
    for (const { name } of startupCacheNames(stableId)) {
      if (!caches.has(name)) {
        ops.push({ type: 'put', key: storageKey(name), value: encode(JSON.stringify(cacheResult(stableId, undefined, resources.configKind))) });
        changed++;
      }
    }
    ops.push({ type: 'put', key: storageKey(STABLE_ID_KEY), value: encode(JSON.stringify(stableId)) });
    // Chromium's per-origin metadata must cover the encoded key/value sizes,
    // including the one-byte string encoding markers.
    const originValues = new Map(rows.filter(([key]) => key.subarray(0, KEY_PREFIX.length).equals(KEY_PREFIX)).map(([key, value]) => [key.toString('hex'), { key, value }]));
    for (const op of ops) originValues.set(op.key.toString('hex'), op);
    const usage = [...originValues.values()].reduce((sum, op) => sum + op.key.length - KEY_PREFIX.length + 1 + op.value.length, 0);
    const time = BigInt(Date.now()) * 1000n + 11644473600000000n;
    ops.push({ type: 'put', key: Buffer.from('META:' + ORIGIN), value: Buffer.concat([Buffer.from([8]), varint(time), Buffer.from([16]), varint(usage)]) });
    ops.push({ type: 'put', key: Buffer.from('METAACCESS:' + ORIGIN), value: Buffer.concat([Buffer.from([8]), varint(time)]) });
    try {
      if (await db.get(Buffer.from('VERSION')) == null) ops.push({ type: 'put', key: Buffer.from('VERSION'), value: Buffer.from('1') });
    }
    catch (e) { if (e.code !== 'LEVEL_NOT_FOUND') throw e; ops.push({ type: 'put', key: Buffer.from('VERSION'), value: Buffer.from('1') }); }
    await db.batch(ops, { sync: true });
    if (!stateId) {
      if (fs.existsSync(stateFile)) fs.copyFileSync(stateFile, stateFile + '.dd-chinese-backup-' + timestamp);
      state['statsig-stable-id'] = stableId;
      const temporary = stateFile + '.dd-chinese-' + crypto.randomBytes(4).toString('hex');
      fs.writeFileSync(temporary, JSON.stringify(state), { flag: 'wx' });
      fs.renameSync(temporary, stateFile);
    }
    // Read back through LevelDB before reporting success.
    for (const op of ops) {
      const value = await db.get(op.key);
      if (!value.equals(op.value)) throw new Error('中文配置回读校验失败。');
    }
    const startupId = JSON.parse(decode(await db.get(storageKey(STABLE_ID_KEY))));
    if (startupId !== stableId) throw new Error('客户端启动身份与中文缓存不匹配。');
    for (const { name, mode } of startupCacheNames(stableId)) {
      const value = JSON.parse(decode(await db.get(storageKey(name))));
      if (localeValue(value, resources.configKind)?.enable_i18n !== true) throw new Error(mode + '中文缓存校验失败。');
    }
  } finally { await db.close(); }
  return { changed, backup, directory, resources };
}
async function inspectOffline({ moduleRoot, appRoot, roamingRoot, userDataRoot }) {
  const resources = archiveInfo(path.join(appRoot, 'resources', 'app.asar'));
  const { userData, directory } = profilePaths({ appRoot, roamingRoot, userDataRoot });
  const lines = ['中文资源：' + resources.locale, '界面中文开关读取位置：' + resources.configKind + '（已依据客户端代码确认）', '语言界面模块：' + resources.providerAsset, '检查的语言缓存目录：' + directory];
  if (!fs.existsSync(path.join(directory, 'CURRENT'))) return lines.concat('语言缓存：尚未创建。').join('\n');
  // Inspect a copy: diagnostic collection never opens the customer's live DB.
  const snapshot = fs.mkdtempSync(path.join(require('node:os').tmpdir(), 'DDCodexChineseCheck-'));
  const copy = path.join(snapshot, 'leveldb');
  const { ClassicLevel } = require(path.join(moduleRoot, 'classic-level'));
  let db;
  try {
    fs.cpSync(directory, copy, { recursive: true, filter: file => path.basename(file) !== 'LOCK' });
    db = new ClassicLevel(copy, { keyEncoding: 'buffer', valueEncoding: 'buffer' });
    await db.open();
    const raw = await db.get(storageKey(STABLE_ID_KEY));
    const rendererId = raw == null ? null : JSON.parse(decode(raw));
    const statePath = path.join(userData, 'statsig-state.json');
    const stateId = fs.existsSync(statePath) ? JSON.parse(fs.readFileSync(statePath, 'utf8').replace(/^\uFEFF/, ''))['statsig-stable-id'] : null;
    lines.push('主进程与界面启动标识：' + (rendererId && stateId && rendererId === stateId ? '一致' : '缺失或不一致'));
    const startupId = typeof stateId === 'string' && stateId.length > 0 ? stateId : rendererId;
    const currentNames = new Set();
    let currentPresent = 0, currentEnabled = 0;
    if (startupId) {
      const groups = new Map();
      for (const { name, mode } of startupCacheNames(startupId)) {
        currentNames.add(name);
        const value = await db.get(storageKey(name));
        const enabled = value != null && localeValue(JSON.parse(decode(value)), resources.configKind)?.enable_i18n === true;
        if (value != null) currentPresent++;
        if (enabled) currentEnabled++;
        groups.set(mode, (groups.get(mode) ?? 0) + (enabled ? 1 : 0));
      }
      for (const [mode, count] of groups) lines.push(mode + '中文缓存：' + count + '/2 条启用');
    }
    let enabled = 0, disabled = 0, other = 0;
    for (const [key, value] of await db.iterator().all()) {
      if (!key.subarray(0, KEY_PREFIX.length).equals(KEY_PREFIX)) continue;
      const name = key.subarray(KEY_PREFIX.length).toString('latin1');
      if (!name.startsWith('statsig.cached.evaluations.')) continue;
      if (!currentNames.has(name)) other++;
      if (localeValue(JSON.parse(decode(value)), resources.configKind)?.enable_i18n === true) enabled++; else disabled++;
    }
    lines.push('全部界面功能缓存：中文启用 ' + enabled + ' 条，未启用 ' + disabled + ' 条');
    if (startupId) lines.push('当前启动标识所需缓存：已存在 ' + currentPresent + '/4 条，中文启用 ' + currentEnabled + '/4 条；其它缓存 ' + other + ' 条（不计入当前模式的启用数量）。');
  } finally {
    if (db) await db.close();
    fs.rmSync(snapshot, { recursive: true, force: true });
  }
  return lines.join('\n');
}
function formatFailure(error) {
  const chain = [], seen = new Set();
  for (let item = error; item && !seen.has(item) && chain.length < 8; item = item.cause) { seen.add(item); chain.push(item); }
  const codes = [...new Set(chain.map(item => item.code).filter(code => typeof code === 'string' && /^(LEVEL_[A-Z_]+|EACCES|EPERM|EBUSY|ENOENT)$/.test(code)))];
  const messages = chain.map(item => String(item.message || '')).join('\n');
  let reason;
  if (codes.includes('LEVEL_LOCKED') || codes.includes('EBUSY') || /(?:lock|LOCK).*(?:already held|being used|another process)|(?:being used|another process).*LOCK/i.test(messages))
    reason = '缓存目录被其他进程占用；请完全退出 Codex 及其它访问该目录的程序后重试。';
  else if (codes.includes('EACCES') || codes.includes('EPERM') || /access is denied|permission denied|拒绝访问/i.test(messages))
    reason = '缓存目录访问被拒绝；请使用安装 Codex 的同一 Windows 用户检查目录访问权限。';
  else if (codes.includes('LEVEL_CORRUPTION') || /corruption:|corrupted|bad record|bad magic number/i.test(messages))
    reason = '缓存数据库报告数据损坏；助手未清空或重建该目录，将继续处理其它已有目录。';
  else if (codes.includes('ENOENT') || /(?:MANIFEST|CURRENT).*(?:not found|cannot find|No such file)|(?:not found|cannot find|No such file).*(?:MANIFEST|CURRENT)/i.test(messages))
    reason = '缓存目录或数据库所需文件缺失；助手未清空或重建该目录，将继续处理其它已有目录。';
  else if (codes.some(code => code.startsWith('LEVEL_')) || /Database failed to open/i.test(messages))
    reason = '缓存数据库无法打开；仅凭此结果不能确定是占用、权限还是文件异常，将继续处理其它已有目录。';
  if (!reason) return error.message;
  return reason + (codes.length ? ' 底层错误码：' + codes.join(' / ') + '。' : '');
}
module.exports = { hash, encode, decode, storageKey, patchPayload, cacheResult, archiveInfo, localeLookupKind, enableOffline, inspectOffline, startupCacheNames, localeValue, profilePaths, formatFailure, SDK_KEY, STABLE_ID_KEY };
if (require.main === module) {
  const [moduleRoot, appRoot, roamingRoot, mode, userDataRoot] = process.argv.slice(2);
  if (!moduleRoot || !appRoot || !roamingRoot) throw new Error('Missing runtime or profile path');
  (mode === '--inspect' ? inspectOffline({ moduleRoot, appRoot, roamingRoot, userDataRoot }) : enableOffline({ moduleRoot, appRoot, roamingRoot, userDataRoot })).then(result => {
    if (mode === '--inspect') { console.log(result); return; }
    console.log('已写入离线中文启用配置，已校验 ' + result.changed + ' 条语言缓存。');
    console.log('客户端启动标识与语言缓存对应关系已校验。');
    console.log('未登录/无需登录模式与 API 密钥模式：均已准备中文缓存。');
    console.log('界面中文开关读取位置：' + result.resources.configKind + '（已依据客户端代码确认）');
    console.log('语言界面模块：' + result.resources.providerAsset);
    console.log('中文资源：' + result.resources.locale);
    console.log('写入的语言缓存目录：' + result.directory);
    if (result.backup) console.log('原缓存备份：' + result.backup);
  }).catch(error => { console.error(formatFailure(error)); process.exitCode = 1; });
}
