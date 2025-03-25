import React, { useEffect, ReactNode, RefObject, useState, useRef } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
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
    shouldAutoScroll
}: ChatContainerProps) => {
    const [copiedMsgId, setCopiedMsgId] = useState<string | null>(null);
    const [canScrollUp, setCanScrollUp] = useState<boolean>(false);
    const [canScrollDown, setCanScrollDown] = useState<boolean>(false);
    const containerRef = useRef<HTMLDivElement | null>(null);

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

    // Check if container can scroll and show/hide shadows accordingly
    const checkScroll = () => {
        if (messagesEndRef.current) {
            const { scrollTop, scrollHeight, clientHeight } = messagesEndRef.current;
            
            // Show top shadow if we're not at the top (more sensitive threshold)
            setCanScrollUp(scrollTop > 5);
            
            // Show bottom shadow if we're not at the bottom (more sensitive threshold)
            setCanScrollDown(scrollTop + clientHeight < scrollHeight - 5);
        }
    };

    // Scroll only if near the bottom
    useEffect(() => {
        if (shouldAutoScroll && messagesEndRef.current) {
            messagesEndRef.current.scrollIntoView({ behavior: 'smooth' });
        }
    }, [messages, shouldAutoScroll, messagesEndRef]);

    // Setup scroll listener
    useEffect(() => {
        const currentRef = messagesEndRef.current;
        
        if (currentRef) {
            // Initial check
            checkScroll();
            
            // Add scroll listener
            currentRef.addEventListener('scroll', checkScroll);
            
            // Check after content changes
            const observer = new MutationObserver(checkScroll);
            observer.observe(currentRef, { childList: true, subtree: true });
            
            return () => {
                currentRef.removeEventListener('scroll', checkScroll);
                observer.disconnect();
            };
        }
    }, []);

    // Check scroll state on content changes
    useEffect(() => {
        checkScroll();
    }, [messages]);

    return (
        <div 
            ref={containerRef} 
            className={`chat-container ${canScrollUp ? 'can-scroll-up' : ''} ${canScrollDown ? 'can-scroll-down' : ''}`}
        >
            {/* Top shadow indicator */}
            <div className={`scroll-shadow-top ${canScrollUp ? 'visible' : ''}`} />
            
            <div ref={messagesEndRef} className="messages-container" onScroll={checkScroll}>
                {messages.map(msg => (
                    <div 
                        key={msg.responseId} 
                        className={`message ${msg.role}`}
                        data-type={msg.type}
                    >
                        <div className="message-container">
                            <div className="message-content">
                                <ReactMarkdown 
                                    remarkPlugins={[
                                        remarkGfm,
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
                        </div>
                    </div>
                ))}
            </div>
            
            {/* Form container with positioned shadow */}
            <div className="form-container">
                {/* Bottom shadow indicator positioned above the form */}
                <div className={`scroll-shadow-bottom ${canScrollDown ? 'visible' : ''}`} />
                
                <form onSubmit={handleSubmit} className="message-form" autoComplete="off">
                    <input
                        type="text"
                        value={prompt}
                        onChange={e => setPrompt(e.target.value)}
                        placeholder="How can I help you with your travel plans?"
                        disabled={streamingMessageId ? true : false}
                        className="message-input"
                        autoComplete="off"
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
        </div>
    );
};

export default ChatContainer;
