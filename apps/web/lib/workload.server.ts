import "server-only";
import { request as httpsRequest, Agent } from "node:https";
import { request as httpRequest } from "node:http";
import {
  CognitoIdentityClient,
  GetOpenIdTokenForDeveloperIdentityCommand,
} from "@aws-sdk/client-cognito-identity";
import { HttpResponse, type HttpRequest } from "@smithy/protocol-http";
import type { HttpHandlerOptions } from "@smithy/types";

const failure = "SaveIT workload admission is unavailable.";
const forbidden = [
  "AWS_CONTAINER_CREDENTIALS_FULL_URI",
  "AWS_CONTAINER_AUTHORIZATION_TOKEN",
  "AWS_CONTAINER_AUTHORIZATION_TOKEN_FILE",
  "AWS_ACCESS_KEY_ID",
  "AWS_SECRET_ACCESS_KEY",
  "AWS_SESSION_TOKEN",
  "AWS_PROFILE",
  "AWS_DEFAULT_PROFILE",
  "AWS_SHARED_CREDENTIALS_FILE",
  "AWS_CONFIG_FILE",
  "AWS_WEB_IDENTITY_TOKEN_FILE",
  "AWS_ROLE_ARN",
  "AWS_ENDPOINT_URL",
  "AWS_ENDPOINT_URL_COGNITO_IDENTITY",
];
type Environment = Record<string, string | undefined>;
type Wire = (
  url: URL,
  method: string,
  headers: Record<string, string>,
  body: string | Uint8Array | undefined,
  limit: number,
  signal: AbortSignal,
) => Promise<Uint8Array>;

// Dedicated agents/literal metadata; no ambient proxy, redirect, cookie or retry.
export const boundedWorkloadWire: Wire = (
  url,
  method,
  headers,
  body,
  limit,
  signal,
) =>
  new Promise((resolve, reject) => {
    const agent =
      url.protocol === "https:"
        ? new Agent({
            keepAlive: false,
            maxSockets: 1,
            rejectUnauthorized: true,
          })
        : undefined;
    const request = (url.protocol === "https:" ? httpsRequest : httpRequest)(
      url,
      { method, headers, signal, agent: agent ?? false, maxHeaderSize: 16384 },
      (response) => {
        const chunks: Buffer[] = [];
        let count = 0;
        const fail = () => {
          response.destroy();
          request.destroy();
          reject(new Error(failure));
        };
        if (
          response.statusCode !== 200 ||
          response.headers["content-encoding"] ||
          response.headers.trailer ||
          Number(response.headers["content-length"] ?? 0) > limit
        ) {
          fail();
          return;
        }
        response.on("data", (chunk) => {
          count += chunk.length;
          if (count > limit) fail();
          else chunks.push(chunk);
        });
        response.on("end", () => {
          if (
            !response.complete ||
            Object.keys(response.trailers).length ||
            count === 0
          ) {
            fail();
            return;
          }
          resolve(Buffer.concat(chunks, count));
        });
        response.on("error", () => reject(new Error(failure)));
      },
    );
    request.setTimeout(3000, () => request.destroy(new Error(failure)));
    request.on("error", () => reject(new Error(failure)));
    request.on("close", () => agent?.destroy());
    request.end(body);
  });

// Only called by the server proxy/readiness route. Never returns a token to UI.
async function mintToken(
  environment: Environment = process.env,
  wire: Wire = boundedWorkloadWire,
): Promise<string | null> {
  const fields = [
    environment.SAVEIT_WORKLOAD_POOL_ID,
    environment.SAVEIT_WORKLOAD_PROVIDER,
    environment.SAVEIT_WORKLOAD_ACCOUNT_ID,
  ];
  if (fields.every((x) => x === undefined)) return null;
  try {
    const [pool, provider, account] = fields as string[];
    if (
      !/^[a-z]{2}-[a-z]+-[1-9]:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(
        pool,
      ) ||
      pool.startsWith("cn-") ||
      !/^saveit\.[a-z][a-z0-9]{1,11}\.(staging|production)\.web$/.test(
        provider,
      ) ||
      !/^[0-9]{12}$/.test(account) ||
      forbidden.some((x) => !!environment[x])
    )
      throw new Error();
    const [, cell, stage] = provider.split(".");
    const relative = environment.AWS_CONTAINER_CREDENTIALS_RELATIVE_URI;
    if (
      !relative ||
      !/^\/v2\/credentials\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(
        relative,
      )
    )
      throw new Error();
    const signal = AbortSignal.timeout(5000);
    const metadata = await wire(
      new URL("http://169.254.170.2" + relative),
      "GET",
      {},
      undefined,
      32768,
      signal,
    );
    let credentials;
    try {
      const value = JSON.parse(
        new TextDecoder("utf-8", { fatal: true }).decode(metadata),
      );
      if (
        value.RoleArn !==
          `arn:aws:iam::${account}:role/saveit-${cell}-${stage}-web-task` ||
        !/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?Z$/.test(
          value.Expiration,
        ) ||
        !Number.isFinite(Date.parse(value.Expiration)) ||
        Date.parse(value.Expiration) <= Date.now() + 60_000
      )
        throw new Error();
      for (const [name, max] of [
        ["AccessKeyId", 128],
        ["SecretAccessKey", 128],
        ["Token", 16384],
      ] as const)
        if (
          typeof value[name] !== "string" ||
          value[name].length < 1 ||
          value[name].length > max ||
          /[^\x21-\x7e]/.test(value[name])
        )
          throw new Error();
      credentials = {
        accessKeyId: value.AccessKeyId as string,
        secretAccessKey: value.SecretAccessKey as string,
        sessionToken: value.Token as string,
      };
    } finally {
      metadata.fill(0);
    }
    const region = pool.split(":")[0];
    const endpoint = `https://cognito-identity.${region}.amazonaws.com/`;
    const client = new CognitoIdentityClient({
      region,
      endpoint,
      credentials,
      maxAttempts: 1,
      defaultsMode: "standard",
      retryMode: "standard",
      authSchemePreference: ["sigv4"],
      useFipsEndpoint: false,
      useDualstackEndpoint: false,
      ignoreConfiguredEndpointUrls: true,
      userAgentAppId: "SaveITWebWorkload",
      defaultUserAgentProvider: async () => [["SaveITWebWorkload", "1"]],
      logger: { debug() {}, info() {}, warn() {}, error() {} },
      requestHandler: {
        metadata: { handlerProtocol: "http/1.1" },
        async handle(request: HttpRequest, _options?: HttpHandlerOptions) {
          if (
            request.protocol !== "https:" ||
            request.hostname !== `cognito-identity.${region}.amazonaws.com` ||
            (request.port && request.port !== 443) ||
            request.path !== "/" ||
            request.method !== "POST" ||
            Object.keys(request.query ?? {}).length ||
            (typeof request.body !== "string" &&
              !(request.body instanceof Uint8Array)) ||
            (typeof request.body === "string"
              ? Buffer.byteLength(request.body)
              : request.body.byteLength) > 4096 ||
            request.headers["x-amz-target"] !==
              "AWSCognitoIdentityService.GetOpenIdTokenForDeveloperIdentity"
          )
            throw new Error();
          const bytes = await wire(
            new URL(endpoint),
            "POST",
            request.headers,
            typeof request.body === "string"
              ? request.body
              : Buffer.from(
                  request.body.buffer,
                  request.body.byteOffset,
                  request.body.byteLength,
                ),
            32768,
            signal,
          );
          return {
            response: new HttpResponse({
              statusCode: 200,
              headers: { "content-type": "application/x-amz-json-1.1" },
              body: bytes,
            }),
          };
        },
      },
    });
    try {
      const response = await client.send(
        new GetOpenIdTokenForDeveloperIdentityCommand({
          IdentityPoolId: pool,
          Logins: { [provider]: "web" },
          TokenDuration: 60,
        }),
        { abortSignal: signal },
      );
      if (
        !new RegExp(
          `^${region}:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$`,
        ).test(response.IdentityId ?? "") ||
        typeof response.Token !== "string" ||
        response.Token.length > 8192 ||
        !/^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(response.Token)
      )
        throw new Error();
      return response.Token;
    } finally {
      client.destroy();
    }
  } catch {
    throw new Error(failure);
  }
}

// One bounded mint at a time per process; cache for at most20s, never credentials.
export function createWorkloadTokenProvider(
  environment: Environment,
  wire: Wire = boundedWorkloadWire,
) {
  let pending: Promise<string | null> | undefined;
  let cached:
    { token: string | null; until: number; binding: string } | undefined;
  return async (signal?: AbortSignal): Promise<string | null> => {
    if (
      signal?.aborted ||
      (forbidden.some((name) => !!environment[name]) &&
        environment.SAVEIT_WORKLOAD_POOL_ID !== undefined)
    )
      throw new Error(failure);
    const binding = JSON.stringify([
      environment.SAVEIT_WORKLOAD_POOL_ID,
      environment.SAVEIT_WORKLOAD_PROVIDER,
      environment.SAVEIT_WORKLOAD_ACCOUNT_ID,
    ]);
    if (cached?.binding === binding && cached.until > Date.now())
      return cached.token;
    if (!pending)
      pending = mintToken(environment, wire)
        .then((token) => {
          if (
            binding !==
            JSON.stringify([
              environment.SAVEIT_WORKLOAD_POOL_ID,
              environment.SAVEIT_WORKLOAD_PROVIDER,
              environment.SAVEIT_WORKLOAD_ACCOUNT_ID,
            ])
          )
            throw new Error(failure);
          cached = { token, until: Date.now() + 20_000, binding };
          return token;
        })
        .finally(() => {
          pending = undefined;
        });
    let abort: (() => void) | undefined;
    try {
      return await Promise.race([
        pending,
        new Promise<never>((_, reject) => {
          abort = () => reject(new Error(failure));
          signal?.addEventListener("abort", abort, { once: true });
          if (signal?.aborted) abort();
        }),
      ]);
    } finally {
      if (abort) signal?.removeEventListener("abort", abort);
    }
  };
}

export const workloadToken = createWorkloadTokenProvider(process.env);
