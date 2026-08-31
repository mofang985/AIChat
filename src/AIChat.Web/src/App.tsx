import { useEffect, useMemo, useState } from 'react';

type RecordItem = Record<string, unknown>;
type FieldType = 'text' | 'textarea' | 'number' | 'checkbox' | 'select';
type FormValue = string | boolean;

type FormField = {
  key: string;
  label: string;
  type?: FieldType;
  options?: string[];
  required?: boolean;
  defaultValue?: FormValue | number;
  wide?: boolean;
  placeholder?: string;
};

type ResourceConfig = {
  key: string;
  title: string;
  endpoint: string;
  fields: string[];
  description: string;
  formFields?: FormField[];
};

type ResourceState = {
  items: RecordItem[];
  loading: boolean;
  error: string | null;
};

const resources: ResourceConfig[] = [
  {
    key: 'employees',
    title: '员工管理',
    endpoint: '/api/employees',
    description: '查看员工基础资料，客户端授权在独立页面设置。',
    fields: ['id', 'employeeNo', 'name', 'department', 'isActive', 'createdAt'],
    formFields: [
      { key: 'employeeNo', label: '员工编号', required: true, placeholder: '例如 E001' },
      { key: 'name', label: '员工姓名', required: true },
      { key: 'department', label: '部门' },
      { key: 'phoneNumber', label: '手机号' },
      { key: 'notes', label: '备注', type: 'textarea', wide: true },
      { key: 'isActive', label: '启用', type: 'checkbox', defaultValue: true },
    ],
  },
  {
    key: 'wechat-accounts',
    title: '微信工作号',
    endpoint: '/api/wechat-accounts',
    description: '查看员工个人微信工作号绑定情况。',
    fields: ['displayName', 'weChatId', 'status', 'employeeId', 'createdAt'],
  },
  {
    key: 'device-hosts',
    title: '物理主机',
    endpoint: '/api/device-hosts',
    description: '查看承载 Windows VM 的物理主机。',
    fields: ['hostName', 'assetCode', 'ipAddress', 'status', 'createdAt'],
  },
  {
    key: 'virtual-devices',
    title: 'Windows VM',
    endpoint: '/api/virtual-devices',
    description: '查看每个员工独立 Windows 虚拟机的绑定状态。',
    fields: ['vmName', 'machineCode', 'status', 'employeeId', 'lastSeenAtUtc'],
  },
  {
    key: 'rpa-client-instances',
    title: 'RPA 客户端实例',
    endpoint: '/api/rpa-client-instances',
    description: '查看 RPA 客户端注册和心跳状态。',
    fields: ['clientInstanceKey', 'machineName', 'status', 'lastAccessStatus', 'lastHeartbeatAtUtc'],
  },
  {
    key: 'rpa-tasks',
    title: 'RPA 任务状态',
    endpoint: '/api/rpa-tasks',
    description: '查看 RPA 任务流转状态。',
    fields: ['taskType', 'status', 'customerDisplayName', 'riskResult', 'createdAt'],
  },
  {
    key: 'products',
    title: '商品资料',
    endpoint: '/api/products',
    description: '维护商品编码、卖点、价格文本和检索关键词。',
    fields: ['productCode', 'name', 'category', 'brand', 'priceText', 'isActive', 'createdAt'],
    formFields: [
      { key: 'productCode', label: '商品编码', required: true },
      { key: 'name', label: '商品名称', required: true },
      { key: 'category', label: '类目' },
      { key: 'brand', label: '品牌' },
      { key: 'priceText', label: '价格文本' },
      { key: 'keywords', label: '关键词' },
      { key: 'summary', label: '摘要', type: 'textarea', wide: true },
      { key: 'description', label: '详细说明', type: 'textarea', wide: true },
      { key: 'isActive', label: '启用', type: 'checkbox', defaultValue: true },
    ],
  },
  {
    key: 'faqs',
    title: 'FAQ',
    endpoint: '/api/faqs',
    description: '维护高频客户问题和标准回复。',
    fields: ['question', 'answer', 'category', 'keywords', 'priority', 'isActive'],
    formFields: [
      { key: 'question', label: '问题', type: 'textarea', required: true, wide: true },
      { key: 'answer', label: '答案', type: 'textarea', required: true, wide: true },
      { key: 'category', label: '分类' },
      { key: 'keywords', label: '关键词' },
      { key: 'priority', label: '优先级', type: 'number', defaultValue: 100 },
      { key: 'isActive', label: '启用', type: 'checkbox', defaultValue: true },
    ],
  },
  {
    key: 'after-sale-rules',
    title: '售后规则',
    endpoint: '/api/after-sale-rules',
    description: '维护退换货、保修、物流异常等售后场景规则。',
    fields: ['ruleCode', 'title', 'scenario', 'priority', 'isActive', 'createdAt'],
    formFields: [
      { key: 'ruleCode', label: '规则编码', required: true },
      { key: 'title', label: '标题', required: true },
      { key: 'scenario', label: '场景' },
      { key: 'keywords', label: '关键词' },
      { key: 'priority', label: '优先级', type: 'number', defaultValue: 100 },
      { key: 'content', label: '规则内容', type: 'textarea', required: true, wide: true },
      { key: 'isActive', label: '启用', type: 'checkbox', defaultValue: true },
    ],
  },
  {
    key: 'risk-rules',
    title: '风险规则',
    endpoint: '/api/risk-rules',
    description: '维护高风险关键词和命中后的处理动作。',
    fields: ['ruleName', 'keywords', 'riskLevel', 'action', 'isEnabled'],
    formFields: [
      { key: 'ruleName', label: '规则名称', required: true },
      { key: 'keywords', label: '关键词', required: true },
      { key: 'riskLevel', label: '风险等级', type: 'select', options: ['Low', 'Medium', 'High'], defaultValue: 'High' },
      {
        key: 'action',
        label: '处理动作',
        type: 'select',
        options: ['MarkRisk', 'BlockAutoSend', 'ManualReview'],
        defaultValue: 'ManualReview',
      },
      { key: 'description', label: '说明', type: 'textarea', wide: true },
      { key: 'isEnabled', label: '启用', type: 'checkbox', defaultValue: true },
    ],
  },
  {
    key: 'prompt-templates',
    title: 'Prompt 模板',
    endpoint: '/api/prompt-templates',
    description: '维护 AI 回复建议使用的系统提示词和用户提示词模板。',
    fields: ['templateCode', 'name', 'templateType', 'version', 'isActive'],
    formFields: [
      { key: 'templateCode', label: '模板编码', required: true },
      { key: 'name', label: '模板名称', required: true },
      {
        key: 'templateType',
        label: '模板类型',
        type: 'select',
        options: ['ReplySuggestion', 'RiskReview'],
        defaultValue: 'ReplySuggestion',
      },
      { key: 'version', label: '版本', defaultValue: 'v1' },
      { key: 'systemPrompt', label: '系统 Prompt', type: 'textarea', required: true, wide: true },
      { key: 'userPromptTemplate', label: '用户 Prompt 模板', type: 'textarea', required: true, wide: true },
      { key: 'isActive', label: '启用', type: 'checkbox', defaultValue: true },
    ],
  },
  {
    key: 'llm-provider-configs',
    title: 'AI 模型配置',
    endpoint: '/api/llm-provider-configs',
    description: '只保存模型连接配置和 API Key 环境变量名，不保存明文 Key。',
    fields: ['providerCode', 'providerType', 'displayName', 'modelName', 'apiKeyEnvironmentVariable', 'isEnabled'],
    formFields: [
      { key: 'providerCode', label: 'Provider Code', required: true, placeholder: '例如 deepseek' },
      {
        key: 'providerType',
        label: 'Provider 类型',
        type: 'select',
        options: ['OpenAICompatible', 'DeepSeek', 'Tongyi'],
        defaultValue: 'OpenAICompatible',
      },
      { key: 'displayName', label: '显示名称', required: true },
      { key: 'baseUrl', label: 'Base URL', required: true, placeholder: '例如 https://api.deepseek.com/v1' },
      { key: 'modelName', label: '模型名称', required: true },
      {
        key: 'apiKeyEnvironmentVariable',
        label: 'API Key 环境变量名',
        required: true,
        placeholder: '例如 AIChat__Llm__DeepSeek__ApiKey',
      },
      { key: 'timeoutSeconds', label: '超时秒数', type: 'number', defaultValue: 60 },
      { key: 'notes', label: '备注', type: 'textarea', wide: true },
      { key: 'isEnabled', label: '启用', type: 'checkbox', defaultValue: true },
    ],
  },
  {
    key: 'reply-suggestions',
    title: 'AI 回复记录',
    endpoint: '/api/ai/reply-suggestions',
    description: '查看 AI 回复建议、风险等级和自动发送决策。',
    fields: ['customerQuestion', 'intent', 'riskLevel', 'shouldAutoSend', 'status', 'failureReason', 'createdAt'],
  },
  {
    key: 'ai-request-logs',
    title: 'AI 请求日志',
    endpoint: '/api/ai/request-logs',
    description: '查看 AI 调用状态、耗时、错误和 token 统计。',
    fields: ['requestType', 'providerCode', 'modelName', 'status', 'errorMessage', 'durationMs', 'createdAt'],
  },
];

const navItems = [
  ...resources.map((resource) => ({ key: resource.key, label: resource.title })),
  { key: 'client-access', label: '员工客户端授权' },
  { key: 'knowledge-search', label: '知识库检索' },
  { key: 'ai-reply-create', label: '生成回复建议' },
];

const initialResourceState = Object.fromEntries(
  resources.map((resource) => [
    resource.key,
    { items: [], loading: false, error: null },
  ]),
) as Record<string, ResourceState>;

export function App() {
  const [activeSection, setActiveSection] = useState(resources[0].key);
  const [apiBaseUrl, setApiBaseUrl] = useState(String(import.meta.env.VITE_API_BASE_URL ?? ''));
  const [resourceStates, setResourceStates] = useState(initialResourceState);
  const [accessEmployeeId, setAccessEmployeeId] = useState('');
  const [policyStatus, setPolicyStatus] = useState('Disabled');
  const [validFrom, setValidFrom] = useState('');
  const [validTo, setValidTo] = useState('');
  const [maxDailyUsageMinutes, setMaxDailyUsageMinutes] = useState('');
  const [maxSessionMinutes, setMaxSessionMinutes] = useState('');
  const [pauseReason, setPauseReason] = useState('');
  const [policyMessage, setPolicyMessage] = useState('等待选择员工。');

  const activeResource = useMemo(
    () => resources.find((resource) => resource.key === activeSection),
    [activeSection],
  );

  const onlineClients = resourceStates['rpa-client-instances'].items.filter(
    (item) => item.status === 'Online',
  ).length;

  const runningTasks = resourceStates['rpa-tasks'].items.filter(
    (item) => item.status === 'Running',
  ).length;

  const dashboardItems = [
    { label: '员工', value: resourceStates.employees.items.length },
    { label: '在线客户端', value: onlineClients },
    { label: '运行任务', value: runningTasks },
    { label: '知识条目', value: resourceStates.products.items.length + resourceStates.faqs.items.length },
    { label: 'AI 回复记录', value: resourceStates['reply-suggestions'].items.length },
  ];

  useEffect(() => {
    void refreshAllResources();
  }, []);

  async function refreshAllResources() {
    await Promise.all(resources.map((resource) => refreshResource(resource)));
  }

  async function refreshResource(resource: ResourceConfig) {
    setResourceStates((current) => ({
      ...current,
      [resource.key]: { ...current[resource.key], loading: true, error: null },
    }));

    try {
      const response = await fetch(createApiUrl(resource.endpoint));
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const data = (await response.json()) as RecordItem[];
      setResourceStates((current) => ({
        ...current,
        [resource.key]: { items: Array.isArray(data) ? data : [], loading: false, error: null },
      }));
    } catch (error) {
      setResourceStates((current) => ({
        ...current,
        [resource.key]: {
          ...current[resource.key],
          loading: false,
          error: error instanceof Error ? error.message : '请求失败',
        },
      }));
    }
  }

  async function loadAccessPolicy() {
    if (!accessEmployeeId.trim()) {
      setPolicyMessage('请先填写员工 ID。');
      return;
    }

    try {
      const response = await fetch(createApiUrl(`/api/employees/${accessEmployeeId.trim()}/client-access`));
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const policy = (await response.json()) as RecordItem;
      setPolicyStatus(String(policy.status ?? 'Disabled'));
      setValidFrom(toDateTimeLocal(policy.validFromUtc));
      setValidTo(toDateTimeLocal(policy.validToUtc));
      setMaxDailyUsageMinutes(toOptionalString(policy.maxDailyUsageMinutes));
      setMaxSessionMinutes(toOptionalString(policy.maxSessionMinutes));
      setPauseReason(String(policy.pauseReason ?? ''));
      setPolicyMessage('授权配置已读取。');
    } catch (error) {
      setPolicyMessage(error instanceof Error ? `读取失败：${error.message}` : '读取失败');
    }
  }

  async function saveAccessPolicy() {
    if (!accessEmployeeId.trim()) {
      setPolicyMessage('请先填写员工 ID。');
      return;
    }

    const payload = {
      status: policyStatus,
      validFromUtc: toIsoOrNull(validFrom),
      validToUtc: toIsoOrNull(validTo),
      maxDailyUsageMinutes: toNumberOrNull(maxDailyUsageMinutes),
      maxSessionMinutes: toNumberOrNull(maxSessionMinutes),
      pauseReason: pauseReason.trim() || null,
    };

    try {
      const response = await fetch(createApiUrl(`/api/employees/${accessEmployeeId.trim()}/client-access`), {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      setPolicyMessage('授权配置已保存。');
    } catch (error) {
      setPolicyMessage(error instanceof Error ? `保存失败：${error.message}` : '保存失败');
    }
  }

  function createApiUrl(path: string) {
    const prefix = apiBaseUrl.trim().replace(/\/$/, '');
    return `${prefix}${path}`;
  }

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand">AIChat</div>
        <nav className="nav-list">
          {navItems.map((item) => (
            <button
              type="button"
              key={item.key}
              className={activeSection === item.key ? 'nav-item active' : 'nav-item'}
              onClick={() => setActiveSection(item.key)}
            >
              {item.label}
            </button>
          ))}
        </nav>
      </aside>

      <section className="workspace">
        <header className="page-header">
          <div>
            <p className="eyebrow">M3 Knowledge &amp; AI Base</p>
            <h1>个人微信工作号RPA AI客服管理台</h1>
            <p className="summary">
              本阶段打通知识库录入、关键词检索、AI 结构化回复、风险判断和回复记录。
            </p>
          </div>
          <div className="api-toolbar">
            <label>
              API 地址
              <input
                value={apiBaseUrl}
                onChange={(event) => setApiBaseUrl(event.target.value)}
                placeholder="例如：http://localhost:5000"
              />
            </label>
            <button type="button" className="primary-action" onClick={refreshAllResources}>
              刷新
            </button>
          </div>
        </header>

        <section className="status-grid" aria-label="系统状态">
          {dashboardItems.map((item) => (
            <article className="status-card" key={item.label}>
              <span>{item.label}</span>
              <strong>{item.value}</strong>
            </article>
          ))}
        </section>

        {activeSection === 'client-access' ? (
          <section className="panel">
            <div className="panel-header">
              <div>
                <h2>员工客户端授权</h2>
                <p>用于限制离职、暂停或过期员工继续使用 RPA 客户端。</p>
              </div>
            </div>

            <div className="form-grid">
              <label>
                员工 ID
                <input value={accessEmployeeId} onChange={(event) => setAccessEmployeeId(event.target.value)} />
              </label>
              <label>
                授权状态
                <select value={policyStatus} onChange={(event) => setPolicyStatus(event.target.value)}>
                  <option value="Enabled">启用</option>
                  <option value="Paused">暂停</option>
                  <option value="Disabled">禁用</option>
                </select>
              </label>
              <label>
                授权开始时间
                <input type="datetime-local" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} />
              </label>
              <label>
                授权结束时间
                <input type="datetime-local" value={validTo} onChange={(event) => setValidTo(event.target.value)} />
              </label>
              <label>
                每日最长使用分钟数
                <input value={maxDailyUsageMinutes} onChange={(event) => setMaxDailyUsageMinutes(event.target.value)} />
              </label>
              <label>
                单次最长会话分钟数
                <input value={maxSessionMinutes} onChange={(event) => setMaxSessionMinutes(event.target.value)} />
              </label>
              <label className="wide-field">
                暂停原因
                <input value={pauseReason} onChange={(event) => setPauseReason(event.target.value)} />
              </label>
            </div>

            <div className="button-row">
              <button type="button" onClick={loadAccessPolicy}>
                读取授权
              </button>
              <button type="button" className="primary-action" onClick={saveAccessPolicy}>
                保存授权
              </button>
              <span className="inline-status">{policyMessage}</span>
            </div>
          </section>
        ) : activeSection === 'knowledge-search' ? (
          <KnowledgeSearchPanel createApiUrl={createApiUrl} />
        ) : activeSection === 'ai-reply-create' ? (
          <AiReplyPanel
            createApiUrl={createApiUrl}
            onCreated={() => {
              const suggestions = resources.find((resource) => resource.key === 'reply-suggestions');
              if (suggestions) {
                void refreshResource(suggestions);
              }
            }}
          />
        ) : activeResource ? (
          <ResourcePanel
            resource={activeResource}
            state={resourceStates[activeResource.key]}
            createApiUrl={createApiUrl}
            onRefresh={() => refreshResource(activeResource)}
          />
        ) : null}
      </section>
    </main>
  );
}

function ResourcePanel({
  resource,
  state,
  createApiUrl,
  onRefresh,
}: {
  resource: ResourceConfig;
  state: ResourceState;
  createApiUrl: (path: string) => string;
  onRefresh: () => void;
}) {
  const [formValues, setFormValues] = useState<Record<string, FormValue>>(() => createInitialFormValues(resource));
  const [editingId, setEditingId] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');

  useEffect(() => {
    setFormValues(createInitialFormValues(resource));
    setEditingId(null);
    setMessage('');
  }, [resource]);

  async function saveResource() {
    if (!resource.formFields) {
      return;
    }

    const missingField = resource.formFields.find(
      (field) => field.required && isEmptyValue(formValues[field.key], field.type),
    );
    if (missingField) {
      setMessage(`请填写：${missingField.label}`);
      return;
    }

    setSaving(true);
    setMessage('');

    try {
      const response = await fetch(createApiUrl(editingId ? `${resource.endpoint}/${editingId}` : resource.endpoint), {
        method: editingId ? 'PUT' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(buildPayload(resource.formFields, formValues)),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      setMessage(editingId ? '已保存修改。' : '已新增记录。');
      setFormValues(createInitialFormValues(resource));
      setEditingId(null);
      onRefresh();
    } catch (error) {
      setMessage(error instanceof Error ? `保存失败：${error.message}` : '保存失败');
    } finally {
      setSaving(false);
    }
  }

  function startEdit(item: RecordItem) {
    setEditingId(String(item.id ?? ''));
    setFormValues(createFormValuesFromItem(resource, item));
    setMessage('正在编辑已选记录。');
  }

  function cancelEdit() {
    setEditingId(null);
    setFormValues(createInitialFormValues(resource));
    setMessage('');
  }

  return (
    <section className="panel">
      <div className="panel-header">
        <div>
          <h2>{resource.title}</h2>
          <p>{resource.description}</p>
        </div>
        <button type="button" onClick={onRefresh}>
          刷新列表
        </button>
      </div>

      {resource.formFields ? (
        <div className="inline-form">
          <div className="form-grid">
            {resource.formFields.map((field) => (
              <FormFieldControl
                field={field}
                key={field.key}
                value={formValues[field.key] ?? ''}
                onChange={(value) =>
                  setFormValues((current) => ({
                    ...current,
                    [field.key]: value,
                  }))
                }
              />
            ))}
          </div>
          <div className="button-row">
            <button type="button" className="primary-action" disabled={saving} onClick={saveResource}>
              {editingId ? '保存修改' : '新增记录'}
            </button>
            {editingId ? (
              <button type="button" onClick={cancelEdit}>
                取消编辑
              </button>
            ) : null}
            <span className="inline-status">{message}</span>
          </div>
        </div>
      ) : null}

      {state.error ? <div className="alert">接口暂不可用：{state.error}</div> : null}
      {state.loading ? <div className="empty-state">正在加载...</div> : null}

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              {resource.fields.map((field) => (
                <th key={field}>{field}</th>
              ))}
              {resource.formFields ? <th>操作</th> : null}
            </tr>
          </thead>
          <tbody>
            {state.items.length === 0 ? (
              <tr>
                <td colSpan={resource.fields.length + (resource.formFields ? 1 : 0)}>暂无数据</td>
              </tr>
            ) : (
              state.items.map((item, index) => (
                <tr key={String(item.id ?? index)}>
                  {resource.fields.map((field) => (
                    <td key={field} className={field === 'id' ? 'id-cell' : undefined}>
                      {field === 'id' ? <CopyableId value={item[field]} /> : formatValue(item[field])}
                    </td>
                  ))}
                  {resource.formFields ? (
                    <td>
                      <button type="button" className="table-action" onClick={() => startEdit(item)}>
                        编辑
                      </button>
                    </td>
                  ) : null}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function CopyableId({ value }: { value: unknown }) {
  const text = formatValue(value);
  if (text === '-') {
    return <span>-</span>;
  }

  async function copyId() {
    await navigator.clipboard.writeText(text);
  }

  return (
    <div className="copyable-id">
      <code title={text}>{text}</code>
      <button type="button" className="copy-button" onClick={() => void copyId()}>
        复制
      </button>
    </div>
  );
}

function FormFieldControl({
  field,
  value,
  onChange,
}: {
  field: FormField;
  value: FormValue;
  onChange: (value: FormValue) => void;
}) {
  const className = field.wide || field.type === 'textarea' ? 'wide-field' : undefined;

  if (field.type === 'checkbox') {
    return (
      <label className={className}>
        <span>{field.label}</span>
        <input type="checkbox" checked={Boolean(value)} onChange={(event) => onChange(event.target.checked)} />
      </label>
    );
  }

  if (field.type === 'select') {
    return (
      <label className={className}>
        {field.label}
        <select value={String(value)} onChange={(event) => onChange(event.target.value)}>
          {(field.options ?? []).map((option) => (
            <option value={option} key={option}>
              {option}
            </option>
          ))}
        </select>
      </label>
    );
  }

  if (field.type === 'textarea') {
    return (
      <label className={className}>
        {field.label}
        <textarea
          value={String(value)}
          placeholder={field.placeholder}
          onChange={(event) => onChange(event.target.value)}
        />
      </label>
    );
  }

  return (
    <label className={className}>
      {field.label}
      <input
        type={field.type === 'number' ? 'number' : 'text'}
        value={String(value)}
        placeholder={field.placeholder}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  );
}

function KnowledgeSearchPanel({ createApiUrl }: { createApiUrl: (path: string) => string }) {
  const [query, setQuery] = useState('');
  const [maxResults, setMaxResults] = useState('5');
  const [results, setResults] = useState<RecordItem[]>([]);
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(false);

  async function searchKnowledge() {
    if (!query.trim()) {
      setMessage('请先输入客户问题或关键词。');
      return;
    }

    setLoading(true);
    setMessage('');
    try {
      const response = await fetch(createApiUrl('/api/knowledge/search'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          query: query.trim(),
          searchMode: 'Keyword',
          maxResults: toNumberOrNull(maxResults) ?? 5,
        }),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const data = (await response.json()) as { results?: RecordItem[] };
      setResults(Array.isArray(data.results) ? data.results : []);
      setMessage('检索完成。');
    } catch (error) {
      setMessage(error instanceof Error ? `检索失败：${error.message}` : '检索失败');
    } finally {
      setLoading(false);
    }
  }

  return (
    <section className="panel">
      <div className="panel-header">
        <div>
          <h2>知识库检索</h2>
          <p>按关键词检索商品资料、FAQ、售后规则和知识分块，后续再接入向量检索。</p>
        </div>
      </div>
      <div className="form-grid">
        <label className="wide-field">
          检索内容
          <textarea value={query} onChange={(event) => setQuery(event.target.value)} />
        </label>
        <label>
          最大返回条数
          <input value={maxResults} onChange={(event) => setMaxResults(event.target.value)} />
        </label>
      </div>
      <div className="button-row">
        <button type="button" className="primary-action" disabled={loading} onClick={searchKnowledge}>
          开始检索
        </button>
        <span className="inline-status">{message}</span>
      </div>
      <SimpleTable fields={['sourceType', 'title', 'score', 'snippet']} items={results} />
    </section>
  );
}

function AiReplyPanel({
  createApiUrl,
  onCreated,
}: {
  createApiUrl: (path: string) => string;
  onCreated: () => void;
}) {
  const [customerQuestion, setCustomerQuestion] = useState('');
  const [providerCode, setProviderCode] = useState('');
  const [promptTemplateCode, setPromptTemplateCode] = useState('');
  const [maxKnowledgeResults, setMaxKnowledgeResults] = useState('5');
  const [message, setMessage] = useState('');
  const [result, setResult] = useState<RecordItem | null>(null);
  const [loading, setLoading] = useState(false);

  async function createReplySuggestion() {
    if (!customerQuestion.trim()) {
      setMessage('请先输入客户问题。');
      return;
    }

    setLoading(true);
    setMessage('');
    setResult(null);

    try {
      const response = await fetch(createApiUrl('/api/ai/reply-suggestions'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          customerQuestion: customerQuestion.trim(),
          providerCode: providerCode.trim() || null,
          promptTemplateCode: promptTemplateCode.trim() || null,
          maxKnowledgeResults: toNumberOrNull(maxKnowledgeResults) ?? 5,
        }),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const data = (await response.json()) as RecordItem;
      setResult(data);
      setMessage('回复建议已生成并保存。');
      onCreated();
    } catch (error) {
      setMessage(error instanceof Error ? `生成失败：${error.message}` : '生成失败');
    } finally {
      setLoading(false);
    }
  }

  return (
    <section className="panel">
      <div className="panel-header">
        <div>
          <h2>生成回复建议</h2>
          <p>调用知识库检索、风险规则和 AI Provider，生成可审计的结构化回复建议。</p>
        </div>
      </div>
      <div className="form-grid">
        <label className="wide-field">
          客户问题
          <textarea value={customerQuestion} onChange={(event) => setCustomerQuestion(event.target.value)} />
        </label>
        <label>
          Provider Code
          <input value={providerCode} onChange={(event) => setProviderCode(event.target.value)} />
        </label>
        <label>
          Prompt 模板编码
          <input value={promptTemplateCode} onChange={(event) => setPromptTemplateCode(event.target.value)} />
        </label>
        <label>
          最大知识命中数
          <input value={maxKnowledgeResults} onChange={(event) => setMaxKnowledgeResults(event.target.value)} />
        </label>
      </div>
      <div className="button-row">
        <button type="button" className="primary-action" disabled={loading} onClick={createReplySuggestion}>
          生成回复
        </button>
        <span className="inline-status">{message}</span>
      </div>
      {result ? (
        <div className="result-box">
          <div>
            <strong>状态</strong>
            <span>{formatValue(result.status)}</span>
          </div>
          <div>
            <strong>风险等级</strong>
            <span>{formatValue(result.riskLevel)}</span>
          </div>
          <div>
            <strong>可自动发送</strong>
            <span>{formatValue(result.shouldAutoSend)}</span>
          </div>
          <div className="wide-result">
            <strong>回复内容</strong>
            <p>{formatValue(result.replyText)}</p>
          </div>
          <div className="wide-result">
            <strong>失败或复核原因</strong>
            <p>{formatValue(result.failureReason)}</p>
          </div>
        </div>
      ) : null}
    </section>
  );
}

function SimpleTable({ fields, items }: { fields: string[]; items: RecordItem[] }) {
  return (
    <div className="table-wrap table-offset">
      <table>
        <thead>
          <tr>
            {fields.map((field) => (
              <th key={field}>{field}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {items.length === 0 ? (
            <tr>
              <td colSpan={fields.length}>暂无数据</td>
            </tr>
          ) : (
            items.map((item, index) => (
              <tr key={String(item.id ?? `${item.sourceId ?? 'row'}-${index}`)}>
                {fields.map((field) => (
                  <td key={field}>{formatValue(item[field])}</td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

function createInitialFormValues(resource: ResourceConfig) {
  const values: Record<string, FormValue> = {};

  for (const field of resource.formFields ?? []) {
    if (typeof field.defaultValue === 'boolean') {
      values[field.key] = field.defaultValue;
    } else if (field.defaultValue !== undefined) {
      values[field.key] = String(field.defaultValue);
    } else if (field.type === 'checkbox') {
      values[field.key] = false;
    } else {
      values[field.key] = '';
    }
  }

  return values;
}

function createFormValuesFromItem(resource: ResourceConfig, item: RecordItem) {
  const values = createInitialFormValues(resource);

  for (const field of resource.formFields ?? []) {
    const value = item[field.key];
    if (field.type === 'checkbox') {
      values[field.key] = Boolean(value);
    } else {
      values[field.key] = value === null || value === undefined ? '' : String(value);
    }
  }

  return values;
}

function buildPayload(fields: FormField[], values: Record<string, FormValue>) {
  const payload: Record<string, unknown> = {};

  for (const field of fields) {
    const value = values[field.key];
    if (field.type === 'checkbox') {
      payload[field.key] = Boolean(value);
    } else if (field.type === 'number') {
      payload[field.key] = toNumberOrNull(String(value));
    } else {
      const text = String(value ?? '').trim();
      payload[field.key] = text.length === 0 ? null : text;
    }
  }

  return payload;
}

function isEmptyValue(value: FormValue | undefined, type?: FieldType) {
  if (type === 'checkbox') {
    return false;
  }

  return String(value ?? '').trim().length === 0;
}

function formatValue(value: unknown) {
  if (value === null || value === undefined || value === '') {
    return '-';
  }

  if (typeof value === 'boolean') {
    return value ? '是' : '否';
  }

  if (typeof value === 'object') {
    return JSON.stringify(value);
  }

  return String(value);
}

function toOptionalString(value: unknown) {
  return value === null || value === undefined ? '' : String(value);
}

function toDateTimeLocal(value: unknown) {
  if (typeof value !== 'string' || value.length === 0) {
    return '';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const offsetDate = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
  return offsetDate.toISOString().slice(0, 16);
}

function toIsoOrNull(value: string) {
  if (!value) {
    return null;
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function toNumberOrNull(value: string) {
  if (!value.trim()) {
    return null;
  }

  const numberValue = Number(value);
  return Number.isFinite(numberValue) ? numberValue : null;
}
