import axios, { type AxiosAdapter, type AxiosRequestConfig } from "axios";
import type {
  MessageInfo,
  MessageScope,
  MessageState,
  PagedResult,
  QueueInfo,
  SendScope,
  SubscriptionInfo,
  TopicInfo,
} from "./types";

const baseURL = import.meta.env.VITE_API_BASE_URL ?? "/api";

const flag = (value: unknown) =>
  value === true || value === "true" || value === "1";
const mockFlag = (value: unknown, defaultVal: boolean) =>
  value === undefined ? defaultVal : flag(value);

const useMockAll = flag(import.meta.env.VITE_USE_MOCK);
const useMockQueues =
  useMockAll || mockFlag(import.meta.env.VITE_USE_MOCK_QUEUES, false);
const useMockTopics =
  useMockAll || mockFlag(import.meta.env.VITE_USE_MOCK_TOPICS, false);
const useMockSubs =
  useMockAll || mockFlag(import.meta.env.VITE_USE_MOCK_SUBSCRIPTIONS, false);
const useMockMessages =
  useMockAll || mockFlag(import.meta.env.VITE_USE_MOCK_MESSAGES, false);
const useMockDlq =
  useMockAll || mockFlag(import.meta.env.VITE_USE_MOCK_DLQ, false);
const anyMockEnabled =
  useMockQueues ||
  useMockTopics ||
  useMockSubs ||
  useMockMessages ||
  useMockDlq;

export const apiBaseUrl = baseURL;
export const isMockEnabled = anyMockEnabled;
export const mockMatrix = {
  queues: useMockQueues,
  topics: useMockTopics,
  subs: useMockSubs,
  messages: useMockMessages,
  dlq: useMockDlq,
};

console.log("[SB-Explorer] Configuration:", {
  apiBaseUrl,
  isMockEnabled,
  mockMatrix,
  env: import.meta.env,
});

export const messagePath = (scope: SendScope) => {
  if (scope.type === "queue") return `/queues/${scope.name}/messages`;
  if (scope.type === "topic") return `/topics/${scope.name}/messages`;
  return `/topics/${scope.topic}/subscriptions/${scope.subscription}/messages`;
};

export const dlqPath = (scope: MessageScope) => {
  if (scope.type === "queue") return `/deadletter/queue/${scope.name}/delete`;
  return `/deadletter/subscription/${scope.topic}/${scope.subscription}/delete`;
};

const respond = <T>(data: T, config: AxiosRequestConfig) =>
  Promise.resolve({
    data,
    status: 200,
    statusText: "OK",
    headers: config.headers ?? {},
    config,
    request: {},
  } as any);

// no explicit notFound; fall through to real adapter when not mocking

const mockData = (() => {
  const queues: QueueInfo[] = [
    {
      name: "orders",
      status: "Active",
      activeMessageCount: 2,
      deadLetterMessageCount: 1,
      maxDeliveryCount: 10,
      lockDuration: "00:01:00",
      defaultTtl: "1.00:00:00",
      createdAt: new Date().toISOString(),
    },
    {
      name: "payments",
      status: "Active",
      activeMessageCount: 0,
      deadLetterMessageCount: 0,
      maxDeliveryCount: 5,
      lockDuration: "00:00:45",
      defaultTtl: "1.00:00:00",
      createdAt: new Date().toISOString(),
    },
  ];

  const topics: TopicInfo[] = [
    {
      name: "invoices",
      status: "Active",
      activeMessageCount: 3,
      deadLetterMessageCount: 0,
      createdAt: new Date().toISOString(),
    },
  ];

  const subscriptions: Record<string, SubscriptionInfo[]> = {
    invoices: [
      {
        name: "processor",
        status: "Active",
        activeMessageCount: 2,
        deadLetterMessageCount: 0,
        maxDeliveryCount: 10,
        lockDuration: "00:01:00",
        defaultTtl: "1.00:00:00",
      },
      {
        name: "archiver",
        status: "Active",
        activeMessageCount: 1,
        deadLetterMessageCount: 0,
        maxDeliveryCount: 5,
        lockDuration: "00:00:45",
        defaultTtl: "1.00:00:00",
      },
    ],
  };

  const messages: Record<string, MessageInfo[]> = {
    "queue:orders:active": [
      {
        messageId: "m1",
        bodyPreview: "Order 123",
        body: JSON.stringify({ id: 123, status: "new" }, null, 2),
        enqueuedTime: new Date().toISOString(),
        deliveryCount: 1,
        contentType: "application/json",
      },
      {
        messageId: "m2",
        bodyPreview: "Order 124",
        body: JSON.stringify({ id: 124, status: "new" }, null, 2),
        enqueuedTime: new Date().toISOString(),
        deliveryCount: 1,
        contentType: "application/json",
      },
    ],
    "queue:orders:deadletter": [
      {
        messageId: "dlq-1",
        bodyPreview: "Poison message",
        body: "bad payload",
        deliveryCount: 5,
      },
      {
        messageId: "dlq-2",
        bodyPreview: "Another poison message",
        body: "very bad payload",
        deliveryCount: 7,
      },
    ],
    "subscription:invoices:processor:active": [
      {
        messageId: "s1",
        bodyPreview: "Invoice created",
        body: JSON.stringify({ invoiceId: "inv-1" }, null, 2),
        enqueuedTime: new Date().toISOString(),
      },
    ],
    "subscription:invoices:archiver:active": [
      {
        messageId: "s2",
        bodyPreview: "Invoice archived",
        body: JSON.stringify({ invoiceId: "inv-2" }, null, 2),
        enqueuedTime: new Date().toISOString(),
      },
    ],
  };

  const paginate = (
    key: string,
    _state: MessageState,
    skip = 0,
    take = 25,
  ): PagedResult<MessageInfo> => {
    const items = messages[key] ?? [];
    const slice = items.slice(skip, skip + take);
    return {
      items: slice,
      total: items.length,
      hasMore: skip + take < items.length,
    };
  };

  return { queues, topics, subscriptions, messages, paginate };
})();

const mockAdapter: AxiosAdapter = async (config) => {
  const { method = "get", url, data, params } = config;
  const path = url?.replace(baseURL, "") ?? "";

  // Queues
  if (useMockQueues) {
    if (method === "get" && path === "/queues")
      return respond(mockData.queues, config);
    if (method === "post" && path === "/queues") {
      const payload = typeof data === "string" ? JSON.parse(data) : data;
      const exists = mockData.queues.find((q) => q.name === payload.name);
      if (!exists) {
        mockData.queues.push({
          name: payload.name,
          status: "Active",
          activeMessageCount: 0,
          deadLetterMessageCount: 0,
          maxDeliveryCount: payload.maxDeliveryCount ?? 10,
          lockDuration: payload.lockDuration ?? "00:01:00",
          defaultTtl: payload.defaultTtl ?? "1.00:00:00",
          createdAt: new Date().toISOString(),
        });
      }
      return respond({}, config);
    }
    if (method === "delete" && path?.startsWith("/queues/")) {
      const name = path.split("/")[2];
      const idx = mockData.queues.findIndex((q) => q.name === name);
      if (idx >= 0) mockData.queues.splice(idx, 1);
      return respond({}, config);
    }
  }

  // Topics
  if (useMockTopics) {
    if (method === "get" && path === "/topics")
      return respond(mockData.topics, config);
    if (method === "post" && path === "/topics") {
      const payload = typeof data === "string" ? JSON.parse(data) : data;
      const exists = mockData.topics.find((t) => t.name === payload.name);
      if (!exists) {
        mockData.topics.push({
          name: payload.name,
          status: "Active",
          activeMessageCount: 0,
          deadLetterMessageCount: 0,
          createdAt: new Date().toISOString(),
        });
      }
      return respond({}, config);
    }
    if (
      method === "delete" &&
      path?.startsWith("/topics/") &&
      !path.includes("/subscriptions")
    ) {
      const name = path.split("/")[2];
      const idx = mockData.topics.findIndex((t) => t.name === name);
      if (idx >= 0) mockData.topics.splice(idx, 1);
      delete mockData.subscriptions[name];
      return respond({}, config);
    }

    if (useMockSubs) {
      // Subscriptions
      if (
        method === "get" &&
        path?.startsWith("/topics/") &&
        path.endsWith("/subscriptions")
      ) {
        const topic = path.split("/")[2];
        return respond(mockData.subscriptions[topic] ?? [], config);
      }
      if (
        method === "post" &&
        path?.startsWith("/topics/") &&
        path.endsWith("/subscriptions")
      ) {
        const topic = path.split("/")[2];
        const payload = typeof data === "string" ? JSON.parse(data) : data;
        const list = mockData.subscriptions[topic] ?? [];
        const exists = list.find((s) => s.name === payload.name);
        if (!exists) {
          list.push({
            name: payload.name,
            status: "Active",
            activeMessageCount: 0,
            deadLetterMessageCount: 0,
            maxDeliveryCount: payload.maxDeliveryCount ?? 10,
            lockDuration: payload.lockDuration ?? "00:01:00",
            defaultTtl: payload.defaultTtl ?? "1.00:00:00",
          });
          mockData.subscriptions[topic] = list;
        }
        return respond({}, config);
      }
      if (method === "delete" && path?.includes("/subscriptions/")) {
        const [, , topic, , subName] = path.split("/");
        const list = mockData.subscriptions[topic] ?? [];
        const idx = list.findIndex((s) => s.name === subName);
        if (idx >= 0) list.splice(idx, 1);
        mockData.subscriptions[topic] = list;
        return respond({}, config);
      }
    }
  }

  // Messages
  if (useMockMessages && method === "get" && path?.includes("/messages")) {
    const state = (params?.state as MessageState) ?? "active";
    const skip = Number(params?.skip ?? 0);
    const take = Number(params?.take ?? 25);
    if (path.startsWith("/queues/")) {
      const [, , queueName] = path.split("/");
      const key = `queue:${queueName}:${state}`;
      return respond(mockData.paginate(key, state, skip, take), config);
    }
    if (path.startsWith("/topics/")) {
      const [, , topic, , subName] = path.split("/");
      const key = `subscription:${topic}:${subName}:${state}`;
      return respond(mockData.paginate(key, state, skip, take), config);
    }
  }

  if (useMockMessages && method === "post" && path?.includes("/messages")) {
    // treat as send; just ack
    return respond({}, config);
  }

  if (useMockDlq && method === "post" && path?.includes("/deadletter/")) {
    // bulk delete DLQ
    return respond({}, config);
  }

  const { adapter, ...configWithoutAdapter } = config;
  return axios.request(configWithoutAdapter);
};

export const apiClient = axios.create({
  baseURL,
  timeout: 12000,
  adapter: anyMockEnabled ? (config) => mockAdapter(config as any) : undefined,
});
