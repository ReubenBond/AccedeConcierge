import React, { useEffect, ReactNode, RefObject, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import remarkGithub from 'remark-github';
import remarkBreaks from 'remark-breaks';
import { Message } from '../types/ChatTypes';

interface ChatContainerProps {
    messages: Message[];
    prompt: string;
    setPrompt: (prompt: string) => void;
    handleSubmit: (e: React.FormEvent) => void;
    cancelChat: () => void;
    streamingMessageId: string | null;
    messagesEndRef: RefObject<HTMLDivElement | null>;
    shouldAutoScroll: boolean;
    renderMessages: () => ReactNode;
    chatId: string;
}

const ChatContainer: React.FC<ChatContainerProps> = ({
    messages,
    prompt,
    setPrompt,
    handleSubmit,
    cancelChat,
    streamingMessageId,
    messagesEndRef,
    shouldAutoScroll,
    chatId
}: ChatContainerProps) => {
    // State to track which message was copied
    const [copiedMsgId, setCopiedMsgId] = useState<string | null>(null);
    const [postingMsgId, setPostingMsgId] = useState<string | null>(null);
    const [applyingLabelMsgId, setApplyingLabelMsgId] = useState<string | null>(null);

    // Extract repository info from chatId
    const [owner, repository] = chatId.split('/');
    const remarkGithubConfig = { repository: `${owner}/${repository}` };

    // Function to copy message text to clipboard
    const copyToClipboard = (text: string, msgId: string) => {
        navigator.clipboard.writeText(text).then(
            () => {
                setCopiedMsgId(msgId);
                // Reset copied state after 2 seconds
                setTimeout(() => setCopiedMsgId(null), 2000);
            },
            (err) => {
                console.error('Could not copy text: ', err);
            }
        );
    };

    // Function to post draft response
    const postDraftResponse = async (owner: string, repository: string, issueNumber: string, msgId: string) => {
        try {
            setPostingMsgId(msgId);
            const response = await fetch(`/api/issues/i/draft/${owner}/${repository}/${issueNumber}/accept`, {
                method: 'POST'
            });
            if (!response.ok) {
                throw new Error('Failed to post response');
            }
        } catch (error) {
            console.error('Error posting response:', error);
        } finally {
            setPostingMsgId(null);
        }
    };

    // Function to apply label
    const applyLabel = async (owner: string, repository: string, issueNumber: string, label: string, msgId: string) => {
        try {
            setApplyingLabelMsgId(msgId);
            const response = await fetch(`/api/issues/i/label/${owner}/${repository}/${issueNumber}/add/${label}`, {
                method: 'POST'
            });
            if (!response.ok) {
                throw new Error('Failed to apply label');
            }
        } catch (error) {
            console.error('Error applying label:', error);
        } finally {
            setApplyingLabelMsgId(null);
        }
    };

    // Scroll only if near the bottom
    useEffect(() => {
        if (shouldAutoScroll && messagesEndRef.current) {
            messagesEndRef.current.scrollIntoView({ behavior: 'smooth' });
        }
    }, [messages, shouldAutoScroll, messagesEndRef]);

    return (
        <div className="chat-container">
            <div ref={messagesEndRef} className="messages-container">
                {messages.map(msg => (
                    <div 
                        key={`${chatId}-${msg.responseId}`} 
                        className={`message ${msg.role}`}
                        data-type={msg.type}
                    >
                        <div className="message-container">
                            <div className="message-content">
                                <ReactMarkdown 
                                    remarkPlugins={[
                                        remarkGfm, 
                                        [remarkGithub, remarkGithubConfig],
                                        remarkBreaks
                                    ]}
                                >
                                    {msg.text}
                                </ReactMarkdown>
                                <button 
                                    className={`copy-message-button ${copiedMsgId === msg.responseId ? 'copied' : ''}`}
                                    onClick={() => copyToClipboard(msg.text, msg.responseId)}
                                    aria-label="Copy message"
                                    title="Copy to clipboard"
                                >
                                    {copiedMsgId === msg.responseId ? (
                                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                            <polyline points="20 6 9 17 4 12"></polyline>
                                        </svg>
                                    ) : (
                                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                            <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                                            <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
                                        </svg>
                                    )}
                                </button>
                            </div>
                            {(msg.type === "update-draft" || msg.type === "add-label") && (
                                <div className="message-actions">
                                    {msg.type === "update-draft" && (
                                        <button
                                            className={`post-message-button ${postingMsgId === msg.responseId ? 'posting' : ''}`}
                                            onClick={() => {
                                                const [owner, repository, issueNumber] = chatId.split('/');
                                                if (owner && repository && issueNumber) {
                                                    postDraftResponse(owner, repository, issueNumber, msg.responseId);
                                                }
                                            }}
                                            disabled={postingMsgId !== null}
                                        >
                                            {postingMsgId === msg.responseId ? 'Posting...' : 'Post draft'}
                                        </button>
                                    )}
                                    {msg.type === "add-label" && (
                                        <button
                                            className={`apply-label-button ${applyingLabelMsgId === msg.responseId ? 'applying' : ''}`}
                                            onClick={() => {
                                                const [owner, repository, issueNumber] = chatId.split('/');
                                                if (owner && repository && issueNumber && msg.data) {
                                                    applyLabel(owner, repository, issueNumber, msg.data, msg.responseId);
                                                }
                                            }}
                                            disabled={applyingLabelMsgId !== null}
                                        >
                                            {applyingLabelMsgId === msg.responseId ? 'Applying...' : 'Apply label'}
                                        </button>
                                    )}
                                </div>
                            )}
                        </div>
                    </div>
                ))}
            </div>
            <form onSubmit={handleSubmit} className="message-form" autoComplete="off">
                <input
                    type="text"
                    value={prompt}
                    onChange={e => setPrompt(e.target.value)}
                    placeholder="Enter your message..."
                    disabled={streamingMessageId ? true : false}
                    className="message-input"
                    autoComplete="issues_chat"
                    name="message-input"
                />
                {streamingMessageId ? (
                    <button type="button" onClick={cancelChat} className="message-button">
                        Stop
                    </button>
                ) : (
                    <button type="submit" disabled={streamingMessageId ? true : false} className="message-button">
                        Send
                    </button>
                )}
            </form>
        </div>
    );
};

export default ChatContainer;
