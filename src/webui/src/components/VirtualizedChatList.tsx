import React, { useRef, useEffect } from 'react';
import { VariableSizeList as List } from 'react-window';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import remarkBreaks from 'remark-breaks';
import { Message } from '../types/ChatTypes';

interface VirtualizedChatListProps {
    messages: Message[];
}

const MessageRow = ({ message, style }: { message: Message; style: React.CSSProperties }) => (
    <div 
        style={{ ...style, padding: '10px' }}
        className={`message ${message.role}`}
        data-type={message.type}
    >
        <div className="message-container">
            <div className="message-content">
                <ReactMarkdown 
                    remarkPlugins={[
                        remarkGfm,
                        remarkBreaks
                    ]}
                >
                    {message.text}
                </ReactMarkdown>
                {message.role !== 'user' && message.type !== 'status' && (
                    <button 
                        className="copy-message-button"
                        onClick={() => navigator.clipboard.writeText(message.text)}
                        aria-label="Copy message"
                        title="Copy to clipboard"
                    >
                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                            <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
                        </svg>
                    </button>
                )}
            </div>
        </div>
    </div>
);

function VirtualizedChatList({ messages }: VirtualizedChatListProps) {
    const listRef = useRef<List>(null);

    // Calculate dynamic heights based on content
    const getItemSize = (index: number) => {
        const msg = messages[index];
        // More accurate estimation for enterprise context
        const estimatedLineLength = 80; // characters per line
        const linesOfText = msg.text.split('\n').length;
        const estimatedLines = Math.max(
            linesOfText,
            Math.ceil(msg.text.length / estimatedLineLength)
        );
        
        // Additional height for code blocks
        const codeBlockCount = (msg.text.match(/```/g) || []).length / 2;
        const codeBlockHeight = codeBlockCount * 50; // Extra height for code blocks
        
        return Math.max(80, estimatedLines * 24 + 40 + codeBlockHeight);
    };

    // Scroll to bottom when messages change
    useEffect(() => {
        if (listRef.current && messages.length > 0) {
            listRef.current.scrollToItem(messages.length - 1);
        }
    }, [messages.length]);

    return (
        <List
            ref={listRef}
            height={600}
            width={'100%'}
            itemCount={messages.length}
            itemSize={getItemSize}
            className="virtualized-message-list"
        >
            {({ index, style }) => (
                <MessageRow 
                    message={messages[index]}
                    style={style}
                />
            )}
        </List>
    );
}

export default VirtualizedChatList;
