import { lazy, Suspense, useEffect, useRef, useState, type FC, type SubmitEvent } from 'react';
import { ActionIcon, Alert, Badge, Group, Loader, Paper, Text, Tooltip, Transition } from '@mantine/core';
import { FileText, Send, X } from 'lucide-react';
import type { Options as ReactMarkdownOptions } from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { getAssistantEnabled, streamChat } from '../api/hub';
import type { KbSource } from '../api/types';
import { AutoMateLogo } from './AutoMateLogo';
import Icon from './Icon';
import classes from './AssistantWidget.module.css';

const ReactMarkdown = lazy(() => import('react-markdown')) as FC<ReactMarkdownOptions>;
const REMARK_PLUGINS = [remarkGfm];
// Answers can echo help-article text, so never auto-load images (a URL could carry the chat away) and open links in a
// new tab with no opener or referrer.
const DISALLOWED_ELEMENTS = ['img'];
const MARKDOWN_COMPONENTS: ReactMarkdownOptions['components'] = {
  a: ({ href, title, children }) => (
    <a href={href} title={title} target="_blank" rel="noopener noreferrer">
      {children}
    </a>
  ),
};

interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  sources?: KbSource[];
}

const MAX_CHARS = 2000;

// What the status line says while the server works. Add a label for each of this app's tools.
const STATUS_LABELS: Record<string, string> = {
  searching_kb: 'Searching help articles…',
  thinking: 'Thinking…',
  search_knowledge_base: 'Reading help articles…',
  get_article: 'Reading help articles…',
};

const statusLabel = (key?: string | null) => (key && STATUS_LABELS[key]) || 'Working…';

const SUGGESTIONS = [
  'What can I open from the App Hub?',
  'How do I switch to another organisation?',
  'What does a scheduled fuel surcharge mean?',
  'Where do I find my API key?',
];

/** Auto-mate help chat. Hidden unless the server reports it's switched on. */
export default function AssistantWidget({ pageContext }: { pageContext?: string }) {
  const [enabled, setEnabled] = useState(false);
  const [opened, setOpened] = useState(false);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [draft, setDraft] = useState('');
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const abort = useRef<AbortController | null>(null);
  const bottom = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    let live = true;
    void getAssistantEnabled().then((on) => live && setEnabled(on));
    return () => {
      live = false;
      abort.current?.abort();
    };
  }, []);
  useEffect(() => bottom.current?.scrollIntoView({ block: 'end' }), [messages]);
  useEffect(() => {
    if (opened) input.current?.focus();
  }, [opened]);

  if (!enabled) return null;

  const send = async (text: string) => {
    const question = text.trim();
    if (!question || busy) return;

    const history: ChatMessage[] = [...messages, { role: 'user', content: question }];
    setMessages([...history, { role: 'assistant', content: '' }]);
    setDraft('');
    setError(null);
    setBusy(true);

    const controller = new AbortController();
    abort.current = controller;
    const update = (patch: (last: ChatMessage) => ChatMessage) =>
      setMessages((current) => [...current.slice(0, -1), patch(current[current.length - 1])]);

    try {
      for await (const evt of streamChat(
        { messages: history.map(({ role, content }) => ({ role, content })), pageContext },
        controller.signal,
      )) {
        if (evt.type === 'text_delta' && evt.text) {
          setStatus(null);
          update((last) => ({ ...last, content: last.content + evt.text }));
        } else if (evt.type === 'status') setStatus(statusLabel(evt.text));
        else if (evt.type === 'tool_use') setStatus(statusLabel(evt.toolName));
        else if (evt.type === 'done' && evt.sources?.length) update((last) => ({ ...last, sources: evt.sources ?? undefined }));
        else if (evt.type === 'error') setError(evt.error ?? "Auto-mate couldn't answer. Try again.");
      }
    } finally {
      // Drop an empty assistant bubble left by an error, so the conversation sent next time stays valid.
      setMessages((current) => (current[current.length - 1]?.content ? current : current.slice(0, -1)));
      setStatus(null);
      setBusy(false);
    }
  };

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    void send(draft);
  };

  // Shown from the moment a question is sent until text streams, and again while a later tool call runs.
  const activity = busy ? (status ?? (messages[messages.length - 1]?.content ? null : 'Thinking…')) : null;

  return (
    <>
      {!opened && (
        <Tooltip label="Auto-mate assistant" position="left">
          <ActionIcon
            className={classes.launcher}
            onClick={() => setOpened(true)}
            aria-label="Open Auto-mate assistant"
            variant="filled"
            autoContrast
            radius="xl"
            size={56}
          >
            <AutoMateLogo variant="mono" size={32} />
          </ActionIcon>
        </Tooltip>
      )}

      <Transition mounted={opened} transition="slide-up" duration={200}>
        {(styles) => (
          <Paper
            className={classes.panel}
            style={styles}
            shadow="xl"
            radius="lg"
            role="dialog"
            aria-label="Auto-mate assistant"
            onKeyDown={(e) => e.key === 'Escape' && setOpened(false)}
          >
            <Group className={classes.header} gap="xs" px="md" py="sm" wrap="nowrap">
              <AutoMateLogo size={22} />
              <Text fw={700} size="sm" style={{ flex: 1 }}>
                Auto-mate assistant
              </Text>
              <ActionIcon variant="subtle" color="gray" onClick={() => setOpened(false)} aria-label="Close assistant">
                <Icon icon={X} size={18} />
              </ActionIcon>
            </Group>

            <div className={classes.viewport} aria-live="polite">
              {messages.length === 0 && (
                <>
                  <Text size="sm" c="dimmed">
                    Ask about your apps, organisations, fuel surcharge or API key.
                  </Text>
                  <div className={classes.suggestions}>
                    {SUGGESTIONS.map((prompt) => (
                      <button key={prompt} type="button" className={classes.suggestion} onClick={() => void send(prompt)}>
                        {prompt}
                      </button>
                    ))}
                  </div>
                </>
              )}

              {messages.map((message, index) =>
                message.role === 'user' ? (
                  <div key={index} className={`${classes.msg} ${classes.user}`}>
                    {message.content}
                  </div>
                ) : (
                  message.content && (
                    <div key={index} className={`${classes.msg} ${classes.assistant}`}>
                      <div className={classes.markdown}>
                        <Suspense fallback={<span style={{ whiteSpace: 'pre-wrap' }}>{message.content}</span>}>
                          <ReactMarkdown remarkPlugins={REMARK_PLUGINS} disallowedElements={DISALLOWED_ELEMENTS} components={MARKDOWN_COMPONENTS}>
                            {message.content}
                          </ReactMarkdown>
                        </Suspense>
                      </div>
                      <SourceBadges sources={message.sources} />
                    </div>
                  )
                ),
              )}

              {activity && (
                <Group gap={6} role="status">
                  <Loader size="xs" type="dots" />
                  <Text size="xs" c="dimmed">
                    {activity}
                  </Text>
                </Group>
              )}
              {error && (
                <Alert color="red" role="alert" p="xs">
                  {error}
                </Alert>
              )}
              <div ref={bottom} />
            </div>

            <form className={classes.composer} onSubmit={submit}>
              <textarea
                ref={input}
                className={classes.input}
                aria-label="Your question"
                placeholder="Ask Auto-mate…"
                rows={1}
                maxLength={MAX_CHARS}
                value={draft}
                onChange={(e) => {
                  setDraft(e.currentTarget.value);
                  e.currentTarget.style.height = 'auto';
                  e.currentTarget.style.height = `${e.currentTarget.scrollHeight}px`;
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    void send(draft);
                  }
                }}
              />
              <button type="submit" className={classes.send} aria-label="Send" disabled={busy || !draft.trim()}>
                <Icon icon={Send} size={16} />
              </button>
            </form>
            <Text size="xs" c="dimmed" px="md" pb="xs" mt={-6}>
              Don't share passwords or card details.
            </Text>
          </Paper>
        )}
      </Transition>
    </>
  );
}

function SourceBadges({ sources }: { sources?: KbSource[] }) {
  if (!sources?.length) return null;

  return (
    <Group gap={6} mt={6} role="list" aria-label="Sources">
      {sources.map((source) => (
        <Badge
          key={source.articleId}
          role="listitem"
          variant="outline"
          color="gray"
          size="sm"
          tt="none"
          leftSection={<Icon icon={FileText} size={12} />}
        >
          {source.title}
        </Badge>
      ))}
    </Group>
  );
}
